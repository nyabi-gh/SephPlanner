# SephPlanner 비공개 진단 서버 배포 인계

이 파일은 배포 담당자가 적용할 절차다. 로컬 구현 과정에서는 DNS, VPS Caddy, 홈서버를 변경하지 않았다. 현재 공개된 플러그인 0.2.7에는 업로드 기능이 없으며 새 플러그인은 서버 검증 후 별도로 배포해야 한다.

## 확정한 연결

```text
게임 → HTTPS sephplanner.nyabi.me (139.162.123.174)
     → VPS Caddy (TLS 종료, WireGuard 10.77.0.1)
     → HTTP 10.77.0.2:8081 → 컨테이너 8080
```

홈서버는 Debian 13과 Docker, WireGuard 인터페이스는 양쪽 모두 `wg-edge`다. 기존 Seafile의 홈서버 8080은 사용하지 않는다. DNS A와 DNS only 설정은 이미 준비됐다는 운영자의 확인을 받았다. AAAA와 공개 포트·공유기 포워딩을 추가할 필요가 없다.

| 용도 | 위치 |
|---|---|
| 홈서버 Compose와 게시 파일 | `/opt/sephplanner` |
| 영속 진단 저장 | `/srv/nyas/sephplanner/reports` → `/data/reports` |
| 관리자 토큰 | `/opt/sephplanner/secrets/admin-token.txt` |
| VPS Caddy 조각 | `/etc/caddy/sephplanner.caddy` |
| 홈서버 systemd 단위 | `/etc/systemd/system/sephplanner.service` |

## 준비 파일 만들기

개발 PC에서 `scripts/check.ps1`을 통과한 뒤 `scripts/package-diagnostics.ps1`을 실행한다. `artifacts/diagnostics-<시각>.zip`은 서버용 관리 코드와 설정만 포함한다. 게임 DLL·카탈로그·제보 원문·관리자 토큰은 포함하지 않는다. 압축을 풀면 `publish/`와 Compose 파일이 같은 디렉터리에 있다. 서버에 .NET SDK를 설치할 필요는 없다.

패키지를 사설 경로로 홈서버에 전달하고 `/opt/sephplanner`에 배치한다. `.dockerignore`도 복사한다. 소스 저장소나 플러그인 전체를 Docker 빌드 문맥으로 보내지 않는다.

## 홈서버 배포

아래는 홈서버의 root 셸에서 실행할 절차다. 기존 서비스와 HDD 마운트를 먼저 확인한다. `docker compose version`, `command -v docker`, `systemctl status wg-quick@wg-edge`로 Compose 기능·실행 경로·WireGuard 단위 이름이 제공 파일과 맞는지 확인한다. 기존 환경이 다른 단위로 WireGuard를 관리하면 서비스 파일의 의존성을 그 단위로 변경한다.

```sh
cd /opt/sephplanner
docker compose build --pull
collector_uid=$(docker run --rm --entrypoint id sephplanner-collector:local -u)
collector_gid=$(docker run --rm --entrypoint id sephplanner-collector:local -g)
findmnt -T /srv/nyas
```

`findmnt`가 의도한 HDD를 가리키는 것을 확인한 후 디렉터리를 만든다. 단일 컨테이너만 이 저장소를 사용한다. 시작 시 미완료 `.upload` 파일을 정리하므로 같은 볼륨을 여러 서버 인스턴스에 공유하면 안 된다.

```sh
install -d -m 700 -o "$collector_uid" -g "$collector_gid" /srv/nyas/sephplanner/reports
install -d -m 700 /opt/sephplanner/secrets
if [ ! -e /opt/sephplanner/secrets/admin-token.txt ]; then
    (umask 077; openssl rand -hex 32 > /opt/sephplanner/secrets/admin-token.txt)
fi
chown "$collector_uid:$collector_gid" /opt/sephplanner/secrets/admin-token.txt
chmod 400 /opt/sephplanner/secrets/admin-token.txt
docker compose config --quiet
```

토큰은 컨테이너 사용자에게 읽기만 허용한 파일로 마운트한다. 터미널 출력·Git·플러그인 설정에 넣지 않는다. 이미지의 실제 UID/GID를 조회하므로 숫자를 임의로 가정하지 않는다. Docker 사용자 네임스페이스를 별도로 사용하는 환경이면 호스트 소유권 매핑도 확인한다.

**외부 연결을 열기 전에** 실제 Docker 방화벽 백엔드를 확인하고 `wg-edge`를 통해 VPS `10.77.0.1`에서 홈서버 `10.77.0.2:8081`로 오는 TCP 연결만 허용한다. Docker가 게시 포트에 사용하는 경로에 적용해야 하므로 일반 INPUT 체인 규칙만으로 차단됐다고 판단하지 않는다. 원문에 방화벽 상태가 없으므로 여기서는 임의의 iptables/nft 명령을 제공하지 않는다. 컨테이너에 도착하는 실제 소스도 확인한다. 앱은 `10.77.0.1`의 전달 헤더만 신뢰한다. 예상과 다르면 원인을 확인하고 실제 프록시 주소 하나만 지정하며 모든 네트워크를 신뢰하도록 변경하지 않는다.

```sh
install -m 644 sephplanner.service /etc/systemd/system/sephplanner.service
systemctl daemon-reload
systemctl enable --now sephplanner.service
docker compose ps
```

컨테이너 healthcheck는 내부 HTTP `/health`를 검사한다. systemd는 Docker·WireGuard·HDD 준비 이후 시작하고 정상 상태를 기다린다. `restart: on-failure`는 프로세스 장애 대응에 사용한다. 처음 배포와 Docker 재시작·홈서버 재부팅 이후 모두 서비스 복구를 확인한다.

## VPS Caddy

VPS에서 현재 실행 설정을 확인한다. 전달받은 `/etc/caddy/Caddyfile` 경로는 최근 실행 설정을 다시 읽은 결과는 아니다.

```sh
systemctl show caddy -p ExecStart -p ExecReload
caddy version
```

제공한 `sephplanner.caddy`를 `/etc/caddy/sephplanner.caddy`로 배치하고 **실제 사용 중인** Caddyfile에 해당 파일의 import를 한 번 추가한다. 기존 import와 서비스를 보존한다. 업로드는 multipart 없이 ZIP 본문을 직접 보내므로 Caddy와 앱 제한 모두 8MiB다. 현재 설치된 Caddy가 `request_body`를 지원하는지는 validate로 확인한다.

```sh
caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile
curl --fail http://10.77.0.2:8081/health
systemctl reload caddy
curl --fail https://sephplanner.nyabi.me/health
```

설정 경로가 다르면 위 명령도 맞춰 변경한다. reload 후 기존 Immich·Vaultwarden·Seafile도 정상인지 확인한다. Caddy의 HTTPS 전달 헤더를 앱이 신뢰해야 업로드가 허용된다. 위조된 외부 전달 헤더가 통과하지 않는지도 검사한다.

## 업로드 규약과 인수 검사

`POST /api/v1/reports`, `Content-Type: application/zip`, `X-SephPlanner-Report-Id: <소문자 Guid N 32자리>`, `X-SephPlanner-SHA256: <ZIP SHA-256 소문자 64자리>`다. ZIP은 `report.json`이 필수이며 `inventory-dump.txt`, `inventory-snapshot.json`, `plan.replay`, `sephplanner.log`만 추가로 허용한다. 폴더·중복 항목·잘못된 UTF-8·압축 후 8MiB 초과·압축 해제 후 16MiB 초과를 거부한다. 서버는 파일을 디렉터리에 풀거나 실행하지 않는다.

합성 검사용 `report.json` 예시:

```json
{"Version":1,"ReportId":"11111111111111111111111111111111"}
```

실제 게임 자료를 사용하기 전에 위 설명 파일만 든 ZIP으로 새 접수 201, 동일 바이트 재전송 200, 같은 ID에 다른 내용 409를 확인한다. 성공 본문은 제보 ID 32자다. 인증 없는 관리자 접근은 401, 잘못된 자료는 400, 크기 제한은 413 또는 400, 빈도·동시 처리 제한은 429, 저장소 상한은 507이다. 일반 400은 프록시의 HTTPS 인식 실패일 수도 있다.

클라이언트에 비밀키를 배포하지 않으므로 제출 API는 공개다. ID와 해시는 정상 게임 사용자를 인증하지 않는다. 기본 전역 상한은 분당 30요청·동시 2개·60초 처리·저장 512MiB·1000건이다. 이 수치는 초기 운영 추정값이며 실제 수집량을 보며 `Diagnostics__UploadsPerMinute`, `Diagnostics__ConcurrentUploads`, `Diagnostics__UploadTimeoutSeconds`, `Diagnostics__MaximumStorageBytes`, `Diagnostics__MaximumReports`로 조정한다. 운영 중 남용이나 상한 도달을 감시한다. 원문은 관리자 인증 후에만 읽을 수 있다.

14일이 지난 자료는 시작 시·매시간·목록/다운로드/저장 전에 삭제한다. `Diagnostics__RetentionDays`로 1~14일을 지정할 수 있다. 서버 백업을 만든다면 백업에도 별도로 같은 삭제 정책을 적용해야 한다. 클라이언트 로컬 폴더는 자동 삭제하지 않는다.

## 관리자 조회·삭제

서버 토큰으로 `Authorization: Bearer <토큰>`을 보낸다. `/admin/reports?offset=0`은 최대 100건을 반환하고, `/admin/reports/<ID>` GET은 ZIP, DELETE는 즉시 삭제다. 본문과 토큰을 접근 로그에 기록하지 않는다.

홈서버 root에서 토큰을 명령 인자에 노출하지 않고 목록을 확인하는 예:

```sh
{ printf 'header = "Authorization: Bearer '; tr -d '\r\n' < /opt/sephplanner/secrets/admin-token.txt; printf '"\n'; } |
    curl --fail --config - https://sephplanner.nyabi.me/admin/reports
```

같은 방식으로 URL을 `/admin/reports/<ID>`로 바꾸고 `--output <로컬 파일.zip>` 또는 `--request DELETE`를 지정한다. 관리자가 받은 ZIP과 재현 자료도 공개 저장소나 공개 제보에 올리지 않는다. 토큰 교체는 새 무작위 파일을 준비하고 같은 권한으로 교체한 뒤 컨테이너를 재생성한다. UI에서 전송을 꺼도 이미 접수한 자료가 삭제되지는 않는다. 삭제 요청은 제보 번호로 처리한다.

## 배포 완료 조건

- 실제 Docker 이미지 빌드, 비root 사용자 저장 권한, healthcheck 통과.
- VPS에서 업로드, 외부에서 HTTPS 접수, 관리자 인증·다운로드·삭제 통과.
- VPS 이외의 경로에서 홈서버 게시 포트 차단, 위조 헤더 거부 확인.
- 재시작과 재부팅 후 저장 자료 보존 및 서비스 복구 확인.
- 새 플러그인으로 게임 안에서 미동의·동의·F3 해제·F10 재전송·접수 번호·통신 실패 안내 확인.
- 실제 F10 자료의 개인정보 가림과 `.replay` 재현 검증 후 새 버전 공개.

로컬 Windows의 HTTP 통합 테스트가 위 실서버·Unity 검증을 대체하지 않는다. 이 환경에는 Docker가 설치되어 있지 않아 컨테이너 실행은 배포 담당자가 검증해야 한다.

## 참고 문서

- [ASP.NET Core 전달 헤더와 신뢰 프록시](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0)
- [Docker Compose 서비스·바인드 마운트·상태 검사](https://docs.docker.com/reference/compose-file/services/)
- [Docker Compose 정상 상태 대기](https://docs.docker.com/reference/cli/docker/compose/up/)
- [.NET 컨테이너 비root 사용자](https://learn.microsoft.com/en-us/dotnet/core/compatibility/containers/8.0/app-user)
- [Caddy 요청 본문 크기 제한](https://caddyserver.com/docs/caddyfile/directives/request_body)
