"""로컬 게임 디컴파일과 카탈로그를 대조하는 효과 점검 목록을 만든다."""

import argparse
import collections
import hashlib
import json
from pathlib import Path
import re


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def cell(value):
    return str(value).replace("|", "\\|").replace("\n", " ")


def table(lines, headers, rows):
    lines.append("| " + " | ".join(headers) + " |")
    lines.append("|" + "|".join("---" for _ in headers) + "|")
    for row in rows:
        lines.append("| " + " | ".join(cell(value) for value in row) + " |")
    lines.append("")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--catalog", type=Path, required=True)
    parser.add_argument("--decompiled", type=Path, required=True)
    parser.add_argument("--assembly", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    source = args.decompiled.read_text(encoding="utf-8-sig")
    classes = {}
    for match in re.finditer(
        r"^public (?:abstract |sealed |static )?class (\w+)[^\n]*\n\{", source, re.M
    ):
        end = source.find("\n}", match.end())
        if end < 0:
            raise ValueError("타입 끝을 찾지 못했습니다: " + match[1])
        classes[match[1]] = source[match.start():end + 2]
    charms = read_json(args.catalog / "charms.json")
    tablets = read_json(args.catalog / "tablets.json")
    combos = read_json(args.catalog / "combos.json")
    behaviors = collections.Counter(charm["Behavior"] for charm in charms)
    missing = sorted(set(behaviors) - classes.keys())
    if missing:
        raise ValueError("카탈로그 동작 타입 누락: " + ", ".join(missing))
    for records in (charms, tablets):
        if len({record["EntityId"] for record in records}) != len(records):
            raise ValueError("중복 엔티티가 있습니다.")

    lines = ["# 효과 점검 전체 목록", "",
             "[판정과 수정 우선순위](EFFECT-AUDIT-2026-09-09.md)를 먼저 읽는다.", "",
             "이 목록은 타입·필드·호출 흔적의 전수 대조다. 호출 흔적만으로 효과의 완전 지원이나 전투 효율을 인증하지 않는다.",
             "카탈로그는 저장 당시 프리팹 목록이며, 현재 게임의 활성 프리팹 전수 재수집을 대신하지 않는다.", "",
             f"- 카탈로그 세대: `{args.catalog.name}`",
             f"- 게임 어셈블리 SHA-256: `{digest(args.assembly)}`",
             f"- 디컴파일 SHA-256: `{digest(args.decompiled)}`",
             f"- 아티팩트 {len(charms)}종 / 동작 {len(behaviors)}종 / 석판 {len(tablets)}종 / 콤보 {len(combos)}종", ""]
    for name in ("charms.json", "tablets.json", "combos.json", "stat-measure.json"):
        lines.append(f"- `{name}` SHA-256: `{digest(args.catalog / name)}`")
    lines += ["", "## 아티팩트별 현재 평가 경로", "",
              "정적 표는 능력치 환산 자료다. 파생 클래스의 추가 동작까지 측정됐다는 뜻이 아니다.",
              "모래시계는 현재 코드의 연결 모델을 별도 표기한다. 저장 카탈로그는 형식 9이므로 해당 필드가 없다.", ""]
    supported = {
        "Charm_3Elemental_ByRow": "행 콤보 모델; 행별 추가 능력치 누락",
        "Charm_WhitePaper": "양옆 콤보 모델; 침 연쇄 수정; 종이 중첩은 후속",
        "Charm_UpCharmDamage": "대상·사슬·희귀도 조건; 피해 크기는 추정",
        "Charm_NearLevelDamage": "이웃 표시 레벨 합; 부호·상한 반영",
        "Charm_RightSpellCooldownHelper": "오른쪽 마법 연결; 회복 이득 추정",
        "Charm_PlanetModule": "이웃 행성 수 추정; 대상 조건·활성 후속",
        "Charm_CompanionChaos": "같은 행 동료 수 추정; 대상 활성 후속",
    }
    rows = []
    paths = collections.Counter()
    for charm in sorted(charms, key=lambda record: record["EntityId"]):
        measured = bool(charm.get("StatWorthByLevel"))
        whole = (charm["Behavior"] == "Charm_StatusInstance"
                 and charm.get("StatWorthCoverageKnown", False)
                 and not charm.get("StatWorthUnconverted"))
        route = "정적 표" if measured and whole else "정적 표+추정" if measured else "추정"
        paths[route] += 1
        names = charm.get("Names", {})
        rows.append((charm["EntityId"], names.get("current", charm["Id"]),
                     charm["Behavior"], route, supported.get(charm["Behavior"],
                     "정적 능력치 환산 범위" if whole else "특수 동작별 평가 미구현"),
                     ", ".join(charm.get("StatWorthUnconverted", [])) or "—"))
    lines.append("저장 표 분류: " + ", ".join(f"{key} {value}종" for key, value in sorted(paths.items())) + ".")
    lines.append("")
    table(lines, ["엔티티", "이름", "게임 동작", "저장 표", "현재 별도 모델 / 한계", "미환산 능력치"], rows)
    lines += ["## 동작 클래스별 구조 점검", "",
              "파생 클래스는 부모의 동작도 함께 읽어야 한다. 아래 열은 직접 선언된 코드의 검색 결과다.", ""]
    rows = []
    for behavior, count in sorted(behaviors.items()):
        body = classes[behavior]
        base = body.splitlines()[0].split(":", 1)[-1].strip()
        signals = []
        for label, pattern in [
            ("가방 조회", r"\b(?:Inventory|inventory)\."),
            ("동적 카테고리", r"GetItemCategory|SearchCategory"),
            ("능력치 읽기", r"GetCustomStat|GetStat|\.MaxHp|\.MaxMp"),
            ("능력치 쓰기", r"AddCustomStat|AddStatus|RemoveStatus"),
            ("연결/변형", r"SetEnhancement|SetChaoticMode|AdditionalCost|Additionalcooldown|AddCharmDependency"),
            ("이벤트 구독/값 누적 후보", r"\+=\s*(?:new |[A-Za-z_])"),
            ("주기 갱신", r"\bOnUpdate\(|override void Update\("),
        ]:
            if re.search(pattern, body):
                signals.append(label)
        rows.append((behavior, count, base, ", ".join(signals) or "부모/개별 메서드 확인"))
    table(lines, ["타입", "종수", "상속·인터페이스", "직접 호출 흔적"], rows)
    lines += ["## 석판 전체", ""]
    table(lines, ["엔티티", "이름", "분류", "쿼리", "조건"], (
        (item["EntityId"], item.get("Names", {}).get("current", item["Id"]),
         "인스턴스 쿼리 필요" if item["IsCustom"] else "정적 쿼리",
         item["Query"], item["ConditionQuery"])
        for item in sorted(tablets, key=lambda item: item["EntityId"])))
    lines += ["## 콤보 전체", "",
              "현재 점수는 콤보별 실제 효과량 대신 공통 발동·진행 가치를 쓴다. 아래 단계는 저장 카탈로그 값이며 특수 클래스의 단계 누락 가능성이 있다.", ""]
    table(lines, ["ID", "이름", "저장된 단계"], (
        (item["Id"], item.get("Names", {}).get("current", item["Id"]),
         ", ".join(map(str, item["Thresholds"]))) for item in combos))
    lines += ["## 현재 어셈블리의 추가 동작 타입", "",
              "저장 카탈로그에 연결되지 않은 타입이다. 부모·미사용·비활성 타입이 섞이므로 새 아이템이나 누락 아이템으로 단정하지 않는다.", ""]
    lines += ["- `" + name + "`" for name in sorted(classes)
              if name.startswith("Charm_") and name not in behaviors]
    lines += ["", "## 활성 조건 및 특수 콤보 타입", ""]
    lines += ["- `" + name + "`" for name in sorted(classes)
              if name.startswith(("CharmActivateCriteria_", "ComboEffect_"))]
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"아티팩트 {len(charms)}, 동작 {len(behaviors)}, 석판 {len(tablets)}, 콤보 {len(combos)} 대조 완료")


if __name__ == "__main__":
    main()
