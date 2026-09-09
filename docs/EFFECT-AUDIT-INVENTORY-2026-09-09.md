# 효과 점검 전체 목록

[판정과 수정 우선순위](EFFECT-AUDIT-2026-09-09.md)를 먼저 읽는다.

이 목록은 타입·필드·호출 흔적의 전수 대조다. 호출 흔적만으로 효과의 완전 지원이나 전투 효율을 인증하지 않는다.
카탈로그는 저장 당시 프리팹 목록이며, 현재 게임의 활성 프리팹 전수 재수집을 대신하지 않는다.

- 카탈로그 세대: `20260907T103854027-38738e3fc000497faf30efd6eaadbf25`
- 게임 어셈블리 SHA-256: `c3939a7d431f1362daca655938480569e11f501f7d0820a989873765555a7c39`
- 디컴파일 SHA-256: `4187a6d690383dacd8915019cb23f4690984d8a6b4b8642328a4a4ba66de7e5a`
- 아티팩트 299종 / 동작 136종 / 석판 68종 / 콤보 20종

- `charms.json` SHA-256: `0bbdfa26eba71106fbdf1743a90e3942f58442bb8a58ac39fe0bab8b214d2981`
- `tablets.json` SHA-256: `2b70c1bea56fb5b64f406d5e55dcc5b7b2442921988b19a86891ab748f296c32`
- `combos.json` SHA-256: `15f64edf46a6e468d4d6c88a1634b1f23e9bb90ba8fdeb139d908a44d6dffa9e`
- `stat-measure.json` SHA-256: `624333745478edc312bd21715201c4efcb6979aacdc308d8eb7929ee15247a44`

## 아티팩트별 현재 평가 경로

정적 표는 능력치 환산 자료다. 파생 클래스의 추가 동작까지 측정됐다는 뜻이 아니다.
모래시계는 현재 코드의 연결 모델을 별도 표기한다. 저장 카탈로그는 형식 9이므로 해당 필드가 없다.

저장 표 분류: 정적 표 105종, 정적 표+추정 52종, 추정 142종.

| 엔티티 | 이름 | 게임 동작 | 저장 표 | 현재 별도 모델 / 한계 | 미환산 능력치 |
|---|---|---|---|---|---|
| 1000 | 힘의 부적 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1001 | 재주의 부적 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1002 | 명석함의 부적 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1004 | 부서진 나무뿌리 | Charm_IncreaseMP | 추정 | 특수 동작별 평가 미구현 | — |
| 1005 | 하트 모양 당근 | Charm_CarrotCharm | 추정 | 특수 동작별 평가 미구현 | — |
| 1008 | 악보 '바람' | Charm_IncreaseMeleeAttackRange | 추정 | 특수 동작별 평가 미구현 | — |
| 1009 | 검술 교본 | Charm_IncreaseAttackSpeed | 추정 | 특수 동작별 평가 미구현 | — |
| 1010 | 방패술 교본 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1011 | 바람풀 목도리 | Charm_DashAttackDamage | 추정 | 특수 동작별 평가 미구현 | — |
| 1012 | 용골 파편 | Charm_IncreaseHP | 추정 | 특수 동작별 평가 미구현 | — |
| 1013 | 전격 차크람 | Charm_FireChakram | 추정 | 특수 동작별 평가 미구현 | — |
| 1014 | 축사의 검집 | Charm_AirSlash | 추정 | 특수 동작별 평가 미구현 | — |
| 1015 | 푸른 행성 | Charm_SummonGreenBat | 추정 | 특수 동작별 평가 미구현 | — |
| 1016 | 붉은 이슬 | Charm_Reddew | 추정 | 특수 동작별 평가 미구현 | — |
| 1017 | 방패 귀고리 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1018 | 따뜻한 돌 | Charm_WarmStone | 추정 | 특수 동작별 평가 미구현 | — |
| 1021 | 무뎌진 방울 | Charm_Stun | 추정 | 특수 동작별 평가 미구현 | — |
| 1023 | 말라버린 꽃 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1025 | 교만의 금관 | Charm_FinalComboCritical | 추정 | 특수 동작별 평가 미구현 | — |
| 1026 | 간이 계약서 | Charm_ShortContract | 추정 | 특수 동작별 평가 미구현 | — |
| 1027 | 차가운 자물쇠 | Charm_IncreaseAllDamage | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1028 | 가시 부적 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1029 | 금빛 핸드벨 | Charm_SummonUnit | 추정 | 특수 동작별 평가 미구현 | — |
| 1030 | 다용도 벨트 | Charm_WoodenBox | 추정 | 특수 동작별 평가 미구현 | — |
| 1031 | 엔도디트의 문진 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1032 | 붉은 뱀의 눈 | Charm_FlameGround_Meteor | 추정 | 특수 동작별 평가 미구현 | — |
| 1033 | 검 귀고리 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1034 | 실드 메이트 | Charm_Shieldmate | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1035 | 구름 병 | Charm_LightningPouch | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1036 | 전사의 증표 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1038 | 발화 기름 | Charm_FlameWeapon | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1040 | 하얀 나뭇가지 | Charm_IncreaseMPRegen | 추정 | 특수 동작별 평가 미구현 | — |
| 1048 | 마법 당근 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1049 | 소태도 야쿠모 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1050 | 형광 부적 | Charm_AddBuffFromBlocking | 추정 | 특수 동작별 평가 미구현 | — |
| 1051 | 리포스테 검 조각 | Charm_GuardCounter | 추정 | 특수 동작별 평가 미구현 | — |
| 1053 | 방패 가방 | Charm_AddStatByDefense | 추정 | 특수 동작별 평가 미구현 | — |
| 1055 | 번갯돌 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1056 | 청동 거울 파편 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1058 | 도서관 분침 미니어처 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1059 | 푸른 진주 | Charm_FarChimDamage | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1060 | 세렌의 휘갈긴 편지 | Charm_GainShieldWhileUsingMP | 추정 | 특수 동작별 평가 미구현 | — |
| 1062 | 이각수 관 | Charm_BoltMagicMultiShot | 추정 | 특수 동작별 평가 미구현 | — |
| 1063 | 작은 마법 고둥 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1064 | 마법사의 동전 | Charm_MagicianCoin | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1065 | 돛대 모형 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1066 | 석화 | Charm_Lightning_Range | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1067 | 캘세더니 열쇠 | Charm_3Elemental_ByRow | 추정 | 행 콤보 모델; 행별 추가 능력치 누락 | — |
| 1068 | 사포테 열매 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1069 | 레이에 별조각 | Charm_ReduceMPCost | 추정 | 특수 동작별 평가 미구현 | — |
| 1070 | 뾰족 도토리 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1071 | 미니 발리스타 | Charm_MiniBallista | 추정 | 특수 동작별 평가 미구현 | — |
| 1072 | 잎새 가죽 | Charm_Lightning_BasicAttack | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1073 | 쥐 마법사의 펜던트 | Charm_HomingMagic | 정적 표+추정 | 특수 동작별 평가 미구현 | BOLT_MAGIC_HOMING |
| 1074 | 찌릿 슈크림빵 | Charm_LightningBread | 추정 | 특수 동작별 평가 미구현 | — |
| 1075 | 비어 있는 검 손잡이 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1076 | 마법의 정석 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1077 | 부서진 사파이어 | Charm_BrokenSapphire | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1078 | 인공정령 이피엘 | Charm_IncreaseMpRegenOnGuard | 추정 | 특수 동작별 평가 미구현 | — |
| 1079 | 뾰족 부싯돌 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1080 | 겸손의 왕관 | Charm_IncreaseLastAttackDamage_SwordAndShield | 추정 | 특수 동작별 평가 미구현 | — |
| 1081 | 모형 부리 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1082 | 압박 밴드 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1083 | 열망의 부적 | Charm_IncreaseCriticalChance_NormalAttack | 추정 | 특수 동작별 평가 미구현 | — |
| 1085 | 푸른 고리 | Charm_IncreaseAllDamageByHP | 추정 | 특수 동작별 평가 미구현 | — |
| 1086 | 은접시 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1087 | 크리톤의 인장 | Charm_IncreaseGoldDropRate | 추정 | 특수 동작별 평가 미구현 | — |
| 1088 | 초록색 톱니 | Charm_GainBuffOnMPLoss | 추정 | 특수 동작별 평가 미구현 | — |
| 1089 | 진주 가루 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1090 | 얇은 방석 | Charm_FollowerAttackSpeed | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1091 | 검은 비늘 | Charm_CriticalChanceIncreaseWithTablets | 추정 | 특수 동작별 평가 미구현 | — |
| 1092 | 바람개비 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1093 | 가시덤불 | Charm_DashDamage | 추정 | 특수 동작별 평가 미구현 | — |
| 1094 | 호박석 | Charm_FirstAttackBonusDamage | 추정 | 특수 동작별 평가 미구현 | — |
| 1095 | 깨진 거울 | Charm_BrokenMirror | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1098 | 마법 양동이 | Charm_IncreaseMoveSpeed | 추정 | 특수 동작별 평가 미구현 | — |
| 1099 | 물로 가득 찬 마법 양동이 | Charm_IncreaseMoveSpeed | 추정 | 특수 동작별 평가 미구현 | — |
| 1100 | 용연향 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1101 | 오히아 레후아 | Charm_FrostiumRing | 추정 | 특수 동작별 평가 미구현 | — |
| 1102 | 신갈나무 숯 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1104 | 뇌문 점토판 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1105 | 노란 행성 | Charm_SummonGreenBat | 추정 | 특수 동작별 평가 미구현 | — |
| 1106 | 붉은 행성 | Charm_SummonGreenBat | 추정 | 특수 동작별 평가 미구현 | — |
| 1107 | 하늘색 행성 | Charm_SummonGreenBat | 추정 | 특수 동작별 평가 미구현 | — |
| 1108 | 거대 망원경 | Charm_PlanetModule | 정적 표+추정 | 이웃 행성 수 추정; 대상 조건·활성 후속 | — |
| 1109 | 부정한 붕대 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1111 | 버려진 금반지 | Charm_SweepRange | 추정 | 특수 동작별 평가 미구현 | — |
| 1112 | 절대반지 | Charm_SweepRange | 추정 | 특수 동작별 평가 미구현 | — |
| 1113 | 피뢰침 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1114 | 발리송 | Charm_TooCloseDamage | 추정 | 특수 동작별 평가 미구현 | — |
| 1115 | 갈망의 보주 | Charm_ReservedMPBonus | 추정 | 특수 동작별 평가 미구현 | — |
| 1117 | 소리굽쇠 | Charm_TuningForks | 추정 | 특수 동작별 평가 미구현 | — |
| 1118 | 하얀 알 껍질 | Charm_MinHPKill | 추정 | 특수 동작별 평가 미구현 | — |
| 1119 | 흰 테두리 빵 | Charm_HitInvincible | 추정 | 특수 동작별 평가 미구현 | — |
| 1120 | 혈석 반지 | Charm_KillHeal | 추정 | 특수 동작별 평가 미구현 | — |
| 1121 | 중화제 흑 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1122 | 홍차 잎 주머니 | Charm_MiniBossFight | 추정 | 특수 동작별 평가 미구현 | — |
| 1123 | 스타 루비 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1124 | 스타 아쿠아마린 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1125 | 헬레나의 계단 모형 | Charm_SpeedRun | 추정 | 특수 동작별 평가 미구현 | — |
| 1126 | 황금 단풍잎 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1128 | 붉은 실뭉치 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1129 | 구름씨 화살촉 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1130 | 알폰소의 뿔 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1132 | 초록 잉크병 | Charm_AttackChim | 추정 | 특수 동작별 평가 미구현 | — |
| 1135 | 굴렁쇠용 채 | Charm_DashAttackStack | 추정 | 특수 동작별 평가 미구현 | — |
| 1137 | 볼루스파 | Charm_IceSpear | 추정 | 특수 동작별 평가 미구현 | — |
| 1138 | 물주머니 | Charm_WaterBag | 추정 | 특수 동작별 평가 미구현 | — |
| 1139 | 빙설덩굴 | Charm_CreateBulletOnSweep | 추정 | 특수 동작별 평가 미구현 | — |
| 1140 | 격려의 깃발 | Charm_TheFlagOfCheer | 추정 | 특수 동작별 평가 미구현 | — |
| 1141 | 번개 맞은 나뭇가지 | Charm_FrostiumRing | 추정 | 특수 동작별 평가 미구현 | — |
| 1142 | 가공된 목재 | Charm_IncreaseMoveSpeed | 추정 | 특수 동작별 평가 미구현 | — |
| 1143 | 날개 | Charm_Wings | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1144 | 대립의 천칭 | Charm_FireIce | 추정 | 특수 동작별 평가 미구현 | — |
| 1145 | 푸른 발톱 | Charm_Freeze | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1146 | 친타마니 돌 | Charm_Chintamani | 추정 | 특수 동작별 평가 미구현 | — |
| 1147 | 무지갯빛 깃털 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1148 | 요정의 항아리 | Charm_FairyJar | 추정 | 특수 동작별 평가 미구현 | — |
| 1149 | 금빛 망토 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1150 | 얼어버린 알 | Charm_FrozenEgg | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1151 | 뾰족한 방망이 | Charm_PointedBat | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1152 | 쿠나이 | Charm_Kunai | 추정 | 특수 동작별 평가 미구현 | — |
| 1153 | 화염초 뿌리 | Charm_FlamePlantRoot | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1154 | 맹독 포자 주머니 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1155 | 여섯 잎 클로버 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1156 | 유리 망치 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1157 | 토끼마을 경비병 투구 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1158 | 강화 포션 뚜껑 | Charm_EnhancedPotionCork | 추정 | 특수 동작별 평가 미구현 | — |
| 1159 | 반딧불이 | Charm_FireFly | 추정 | 특수 동작별 평가 미구현 | — |
| 1160 | 녹색 도복 | Charm_GreenGi | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1161 | 아그배나무 열매 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1162 | 매혹하는 루어 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1163 | 마법의 안경 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1164 | 지푸라기 인형 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1165 | 라일리의 회중시계 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1166 | 보석 갑옷 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1167 | 뇌운추적 나침반 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1168 | 빛나는 모래시계 | Charm_RightSpellCooldownHelper | 추정 | 오른쪽 마법 연결; 회복 이득 추정 | — |
| 1169 | 전투 마법사의 장갑 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1170 | 기린의 뿔 | Charm_KirinHorn | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1171 | 오브러스의 피 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1172 | 팔라스의 카드 | Charm_PallasCard | 추정 | 특수 동작별 평가 미구현 | — |
| 1173 | 베루트의 낫 | Charm_ScytheOfBerut | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1174 | 엘루의 낮잠용 베개 | Charm_ElruNaptimePillow | 추정 | 특수 동작별 평가 미구현 | — |
| 1175 | 프로슘 반지 | Charm_FrostiumRing | 추정 | 특수 동작별 평가 미구현 | — |
| 1176 | 눈 결정 목걸이 | Charm_SnowNeckless | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1177 | 푹신한 털장갑 | Charm_WarmGlove | 추정 | 특수 동작별 평가 미구현 | — |
| 1178 | 빙하의 메아리 | Charm_EchoOfTheGlacier | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1179 | 얼음 날개 | Charm_IceWings | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1180 | 얼음갈매기의 발 | Charm_FrozenMemory | 추정 | 특수 동작별 평가 미구현 | — |
| 1181 | 아그마 투영검 190,191번 | Charm_SwordOfLight | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1182 | 오잉크 주술사의 목걸이 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1183 | 붉은 천 조각 | Charm_DebuffDamage | 추정 | 특수 동작별 평가 미구현 | — |
| 1185 | 화염 가리개 | Charm_Burn | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1186 | 순백의 망토 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1187 | 루비 브로치 | Charm_BlazingStormcloud | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1188 | 혈석 귀걸이 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1189 | 무색 큐브 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1190 | 냄비 뚜껑 | Charm_Endure | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1191 | 발터의 작업용 단안경 | Charm_CritAndRanged | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1192 | 가시 등껍질 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1193 | 솔리스 프라투 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1194 | 은팔찌 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1195 | 만화경 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1196 | 생명의 손길 | Charm_Exploitation | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1197 | 고용 문장 '콜린' | Charm_LeadNPC | 추정 | 특수 동작별 평가 미구현 | — |
| 1198 | 헤이타의 영혼 가루 | Charm_SummonUnit | 추정 | 특수 동작별 평가 미구현 | — |
| 1199 | 테로의 영혼 가루 | Charm_SummonUnit | 추정 | 특수 동작별 평가 미구현 | — |
| 1200 | 아마드의 영혼 가루 | Charm_SummonUnit | 추정 | 특수 동작별 평가 미구현 | — |
| 1202 | 채굴 작업 총괄 완장 | Charm_SummonUnit | 추정 | 특수 동작별 평가 미구현 | — |
| 1208 | 눈보라 망치 | Charm_IceHammer | 추정 | 특수 동작별 평가 미구현 | — |
| 1209 | 얼어붙은 심장 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1210 | 아이스 스타 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1211 | 화염충 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1212 | 빙결충 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1213 | 마그마 구슬 | Charm_MagmaBead | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1214 | 봉황의 날개깃 | Charm_FireFeather | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1215 | 천 갑옷 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1216 | 아카데미 브리건딘 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1217 | 도마뱀 판금 갑옷 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1218 | 베고니아 향 주머니 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1220 | 하얀 행성 | Charm_SummonGreenBat | 추정 | 특수 동작별 평가 미구현 | — |
| 1221 | 푸른 손목 보호대 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1223 | 석궁 화살통 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1224 | 재장전용 나무 상자 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1225 | 황동 거울 파편 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1227 | 랜턴 | Charm_Lantern | 추정 | 특수 동작별 평가 미구현 | — |
| 1228 | 명상 서적 | Charm_QuickCast | 정적 표+추정 | 특수 동작별 평가 미구현 | MAGIC_QUICK_CAST |
| 1229 | 고대 모루 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1230 | 물드는 노을 망토 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1231 | 영원의 화로 | Charm_FlameSwordAuto | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1232 | 솔리스 파르보 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1233 | 솔리스 데쿠사 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1234 | 손 거울 | Charm_DashShield | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1235 | 찌르기 교본 | Charm_KatanaEnhancedDashAttackActivator | 추정 | 특수 동작별 평가 미구현 | — |
| 1236 | 운철 귀걸이 | Charm_FlameSwordReturn | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1237 | 운철 어깨 장식 | Charm_FlameSwordFall | 추정 | 특수 동작별 평가 미구현 | — |
| 1238 | 찌릿충 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1239 | 썬더의 귀걸이 | Charm_ElectricEarring | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1240 | 솔리스 데크리 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1241 | 암흑 행성 | Charm_SummonGreenBat | 추정 | 특수 동작별 평가 미구현 | — |
| 1242 | 잿빛 행성 | Charm_SummonGreenBat | 추정 | 특수 동작별 평가 미구현 | — |
| 1243 | 봉인된 테자스 | Charm_StatusInstance | 정적 표+추정 | 특수 동작별 평가 미구현 | BLUE_BURN_CHANGE |
| 1244 | 얼음구름 나비 | Charm_StatusInstance | 정적 표+추정 | 특수 동작별 평가 미구현 | DARK_CLOUD_ICE |
| 1245 | 악보 '폭풍' | Charm_TheTyphoonSheetmusic | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1246 | 바위코끼리 | Charm_RockElephant | 추정 | 특수 동작별 평가 미구현 | — |
| 1247 | 얼어붙은 활 | Charm_IceBow | 추정 | 특수 동작별 평가 미구현 | — |
| 1248 | 플리트비체의 물방울 | Charm_IceSword | 추정 | 특수 동작별 평가 미구현 | — |
| 1249 | 전술 지침서 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1250 | 일렁이는 눈 | Charm_ShadowEye | 추정 | 특수 동작별 평가 미구현 | — |
| 1251 | 악보 '은하' | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1252 | 환록의 망토 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1254 | Item_PlanetComet_Name | Charm_PlanetComet | 추정 | 특수 동작별 평가 미구현 | — |
| 1258 | 하얀 종이 | Charm_WhitePaper | 정적 표+추정 | 양옆 콤보 모델; 침 연쇄 수정; 종이 중첩은 후속 | — |
| 1259 | 영원의 식 | Charm_FireIceWeapon | 정적 표+추정 | 특수 동작별 평가 미구현 | FLAME_SWORD_CALLBACK_FROST |
| 1260 | 우레의 발걸음 | Charm_ThunderousSteps | 추정 | 특수 동작별 평가 미구현 | — |
| 1261 | 플리트비체의 물방울 | Charm_StatusInstance | 정적 표+추정 | 특수 동작별 평가 미구현 | FROST_RELIC_MP_MAXMP_DAMAGE |
| 1262 | 신비한 무게 추 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1263 | 플럭스 결합기 Mk.2 | Charm_CompanionCloud | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1264 | 헌신의 휘장 | Charm_CompanionChaos | 정적 표+추정 | 같은 행 동료 수 추정; 대상 활성 후속 | — |
| 1265 | 자연의 보호 | Charm_MPShieldActive | 추정 | 특수 동작별 평가 미구현 | — |
| 1266 | ... | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1267 | 충격 증폭기 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1268 | 영원한 겨울 | Charm_FreezeNormalSlash | 추정 | 특수 동작별 평가 미구현 | — |
| 1269 | ... | Charm_BurnWeaponDamage | 추정 | 특수 동작별 평가 미구현 | — |
| 1270 | 조화의 수정 | Charm_NearLevelDamage | 추정 | 이웃 표시 레벨 합; 부호·상한 반영 | — |
| 1271 | 행운의 메달 | Charm_TradeMoney | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1272 | 전격의 부적 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1273 | 통통열매 양동이 | Charm_MpHeal | 추정 | 특수 동작별 평가 미구현 | — |
| 1274 | 결함 탐침봉 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1275 | 유랑자의 목걸이 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1276 | 야수의 심장 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1277 | 오색사탕 유리병 | Charm_FireBulletInRange | 추정 | 특수 동작별 평가 미구현 | — |
| 1278 | 돌 가락바퀴 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1279 | 레리드의 전투 팔찌 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1282 | 도시락 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1283 | 용기의 아뮬렛 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1285 | 탄성 밴드 | Charm_EvasionRestore | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1286 | 통나무 | Charm_IncreaseMoveSpeed | 추정 | 특수 동작별 평가 미구현 | — |
| 1287 | 반격의 팔찌 | Charm_AddBuffFromBlocking | 추정 | 특수 동작별 평가 미구현 | — |
| 1288 | 리포스테 봉 파편 | Charm_GuardCounter | 추정 | 특수 동작별 평가 미구현 | — |
| 1289 | 북향의 금빛 침 | Charm_UpCharmDamage | 추정 | 대상·사슬·희귀도 조건; 피해 크기는 추정 | — |
| 1290 | 북향의 푸른 침 | Charm_UpCharmDamage | 추정 | 대상·사슬·희귀도 조건; 피해 크기는 추정 | — |
| 1291 | 아카데미 만년필 | Charm_AddStatByAnotherStat | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1292 | 조로우의 물뿌리개 | Charm_MPMultipleCast | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1293 | 설산 큰귀박쥐 | Charm_IceBat | 정적 표+추정 | 특수 동작별 평가 미구현 | — |
| 1294 | 플라즈마 헬멧 | Charm_StatusInstance | 정적 표+추정 | 특수 동작별 평가 미구현 | PLASMA_ACTIVE |
| 1295 | 붉은행성 관찰일지 | Charm_FlamePlanet | 추정 | 특수 동작별 평가 미구현 | — |
| 1296 | 물의 정령 | Charm_LakeSpirit | 추정 | 특수 동작별 평가 미구현 | — |
| 1297 | 가넷 배지 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1298 | 토파즈 배지 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1299 | 다이아몬드 배지 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1300 | 화난 감자 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1301 | 나그네의 축복 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1302 | 연금술사의 플라스크 | Charm_AlchemyFlask | 추정 | 특수 동작별 평가 미구현 | — |
| 1303 | 혹한의 길로틴 | Charm_Guillotine | 추정 | 특수 동작별 평가 미구현 | — |
| 1304 | 마음의 짐 | Charm_StatusInstance | 추정 | 정적 능력치 환산 범위 | — |
| 1305 | 빨간 버섯 | Charm_AddStatByAnotherStat | 추정 | 특수 동작별 평가 미구현 | — |
| 1306 | 회오리 돌 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1307 | 변환의 서: <tag=TEXT:Elemental_Fire> | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1308 | 변환의 서: <tag=TEXT:Elemental_Ice> | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1309 | 변환의 서: <tag=TEXT:Elemental_Lightning> | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1310 | 저주받은 편지 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 1311 | 서고의 태양 | Charm_StatusInstance | 정적 표+추정 | 특수 동작별 평가 미구현 | FLAME_SWORD_MAGIC_DAMAGE |
| 3000 | 에어 볼트 | Charm_Magic | 추정 | 특수 동작별 평가 미구현 | — |
| 3002 | 파이어 볼트 | Charm_Magic | 추정 | 특수 동작별 평가 미구현 | — |
| 3005 | 회복의 물줄기 | Charm_Magic | 추정 | 특수 동작별 평가 미구현 | — |
| 3011 | 라이트닝 애로우 | Charm_Magic | 추정 | 특수 동작별 평가 미구현 | — |
| 3012 | 파이어 애로우 | Charm_Magic | 추정 | 특수 동작별 평가 미구현 | — |
| 3013 | 불 서커스 | Charm_Magic | 추정 | 특수 동작별 평가 미구현 | — |
| 3015 | 스톤 웨이브 | Charm_Magic | 추정 | 특수 동작별 평가 미구현 | — |
| 3016 | 바위 오잉크 | Charm_Magic | 추정 | 특수 동작별 평가 미구현 | — |
| 3017 | 워터 볼트 | Charm_Magic | 추정 | 특수 동작별 평가 미구현 | — |
| 3018 | 샤프 아이 | Charm_Magic | 추정 | 특수 동작별 평가 미구현 | — |
| 3019 | 헤이스트 | Charm_Magic | 추정 | 특수 동작별 평가 미구현 | — |
| 3020 | 서리 단검 | Charm_Magic | 추정 | 특수 동작별 평가 미구현 | — |
| 3022 | 메테오 샤워 | Charm_Magic | 추정 | 특수 동작별 평가 미구현 | — |
| 3023 | 애로우 레인 | Charm_Magic | 추정 | 특수 동작별 평가 미구현 | — |
| 3024 | ... | Charm_Magic | 추정 | 특수 동작별 평가 미구현 | — |
| 3025 | ... | Charm_Magic | 추정 | 특수 동작별 평가 미구현 | — |
| 3026 | ... | Charm_Magic | 추정 | 특수 동작별 평가 미구현 | — |
| 3027 | 아이스 볼트 | Charm_Magic | 추정 | 특수 동작별 평가 미구현 | — |
| 3028 | 천둥 갑옷 | Charm_Magic | 추정 | 특수 동작별 평가 미구현 | — |
| 3030 | 연막 | Charm_Magic | 추정 | 특수 동작별 평가 미구현 | — |
| 3031 | 축복 | Charm_Magic | 추정 | 특수 동작별 평가 미구현 | — |
| 3032 | 대축복 | Charm_Magic | 추정 | 특수 동작별 평가 미구현 | — |
| 3033 | 보호막 | Charm_Magic | 추정 | 특수 동작별 평가 미구현 | — |
| 3034 | 번개 부메랑 | Charm_Magic | 추정 | 특수 동작별 평가 미구현 | — |
| 3035 | 불 서커스 | Charm_Magic | 추정 | 특수 동작별 평가 미구현 | — |
| 3036 | 천둥의 심판 | Charm_Magic | 추정 | 특수 동작별 평가 미구현 | — |
| 5000 | 경고 문서 | Charm_StatusInstance | 정적 표 | 정적 능력치 환산 범위 | — |
| 5001 | 동행 증표 '아이작' | Charm_LeadNPC | 추정 | 특수 동작별 평가 미구현 | — |
| 5002 | 동행 증표 '카카' | Charm_LeadNPC | 추정 | 특수 동작별 평가 미구현 | — |
| 5003 | 동행 증표 '마루' | Charm_LeadNPC | 추정 | 특수 동작별 평가 미구현 | — |
| 5004 | 동행 증표 '아이바' | Charm_LeadNPC | 추정 | 특수 동작별 평가 미구현 | — |
| 5005 | 동행 증표 '케니' | Charm_LeadNPC | 추정 | 특수 동작별 평가 미구현 | — |
| 5006 | 동행 증표 '필릭스' | Charm_LeadNPC | 추정 | 특수 동작별 평가 미구현 | — |
| 5007 | 동행 증표 '롤프' | Charm_LeadNPC | 추정 | 특수 동작별 평가 미구현 | — |
| 5008 | 동행 증표 '썬더' | Charm_LeadNPC | 추정 | 특수 동작별 평가 미구현 | — |

## 동작 클래스별 구조 점검

파생 클래스는 부모의 동작도 함께 읽어야 한다. 아래 열은 직접 선언된 코드의 검색 결과다.

| 타입 | 종수 | 상속·인터페이스 | 직접 호출 흔적 |
|---|---|---|---|
| Charm_3Elemental_ByRow | 1 | Charm_StatusInstance | 동적 카테고리, 능력치 쓰기 |
| Charm_AddBuffFromBlocking | 2 | Charm_Basic | 이벤트 구독/값 누적 후보 |
| Charm_AddStatByAnotherStat | 2 | Charm_StatusInstance | 능력치 읽기, 능력치 쓰기, 주기 갱신 |
| Charm_AddStatByDefense | 1 | Charm_StatusInstance | 능력치 읽기, 능력치 쓰기, 주기 갱신 |
| Charm_AirSlash | 1 | Charm_Basic, IAttackableCharm | 능력치 읽기, 이벤트 구독/값 누적 후보 |
| Charm_AlchemyFlask | 1 | Charm_StatusInstance | 이벤트 구독/값 누적 후보 |
| Charm_AttackChim | 1 | Charm_StatusInstance | 주기 갱신 |
| Charm_BlazingStormcloud | 1 | Charm_StatusInstance | 가방 조회, 이벤트 구독/값 누적 후보 |
| Charm_BoltMagicMultiShot | 1 | Charm_Basic | 가방 조회, 연결/변형, 이벤트 구독/값 누적 후보 |
| Charm_BrokenMirror | 1 | Charm_StatusInstance | 이벤트 구독/값 누적 후보 |
| Charm_BrokenSapphire | 1 | Charm_StatusInstance | 능력치 쓰기, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_Burn | 1 | Charm_StatusInstance | 이벤트 구독/값 누적 후보 |
| Charm_BurnWeaponDamage | 1 | Charm_Basic | 능력치 쓰기, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_CarrotCharm | 1 | Charm_Basic | 부모/개별 메서드 확인 |
| Charm_Chintamani | 1 | Charm_Basic | 가방 조회, 이벤트 구독/값 누적 후보 |
| Charm_CompanionChaos | 1 | Charm_StatusInstance | 가방 조회, 연결/변형 |
| Charm_CompanionCloud | 1 | Charm_StatusInstance | 가방 조회, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_CreateBulletOnSweep | 1 | Charm_Basic, IAttackableCharm | 능력치 읽기, 주기 갱신 |
| Charm_CritAndRanged | 1 | Charm_StatusInstance | 능력치 쓰기, 주기 갱신 |
| Charm_CriticalChanceIncreaseWithTablets | 1 | Charm_Basic | 가방 조회, 능력치 쓰기, 이벤트 구독/값 누적 후보 |
| Charm_DashAttackDamage | 1 | Charm_Basic | 능력치 쓰기 |
| Charm_DashAttackStack | 1 | Charm_Basic | 능력치 쓰기, 주기 갱신 |
| Charm_DashDamage | 1 | Charm_Basic, IAttackableCharm | 능력치 읽기 |
| Charm_DashShield | 1 | Charm_StatusInstance | 주기 갱신 |
| Charm_DebuffDamage | 1 | Charm_Basic | 이벤트 구독/값 누적 후보 |
| Charm_EchoOfTheGlacier | 1 | Charm_StatusInstance, IAttackableCharm | 능력치 읽기, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_ElectricEarring | 1 | Charm_StatusInstance, IAttackableCharm | 능력치 읽기, 주기 갱신 |
| Charm_ElruNaptimePillow | 1 | Charm_Basic | 부모/개별 메서드 확인 |
| Charm_Endure | 1 | Charm_StatusInstance | 이벤트 구독/값 누적 후보 |
| Charm_EnhancedPotionCork | 1 | Charm_Basic | 능력치 읽기, 능력치 쓰기, 이벤트 구독/값 누적 후보 |
| Charm_EvasionRestore | 1 | Charm_StatusInstance | 이벤트 구독/값 누적 후보 |
| Charm_Exploitation | 1 | Charm_StatusInstance | 주기 갱신 |
| Charm_FairyJar | 1 | Charm_Basic | 부모/개별 메서드 확인 |
| Charm_FarChimDamage | 1 | Charm_StatusInstance | 이벤트 구독/값 누적 후보 |
| Charm_FinalComboCritical | 1 | Charm_Basic | 이벤트 구독/값 누적 후보 |
| Charm_FireBulletInRange | 1 | Charm_Active, IAttackableCharm | 능력치 읽기, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_FireChakram | 1 | Charm_Basic, IAttackableCharm | 능력치 읽기, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_FireFeather | 1 | Charm_StatusInstance, IAttackableCharm | 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_FireFly | 1 | Charm_Basic | 능력치 쓰기, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_FireIce | 1 | Charm_Basic | 능력치 쓰기 |
| Charm_FireIceWeapon | 1 | Charm_StatusInstance | 능력치 쓰기 |
| Charm_FirstAttackBonusDamage | 1 | Charm_Basic | 이벤트 구독/값 누적 후보 |
| Charm_FlameGround_Meteor | 1 | Charm_Basic, IAttackableCharm | 능력치 읽기, 주기 갱신 |
| Charm_FlamePlanet | 1 | Charm_Basic | 능력치 쓰기 |
| Charm_FlamePlantRoot | 1 | Charm_StatusInstance, IAttackableCharm | 능력치 읽기, 주기 갱신 |
| Charm_FlameSwordAuto | 1 | Charm_StatusInstance | 가방 조회, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_FlameSwordFall | 1 | Charm_Active, IAttackableCharm | 가방 조회, 이벤트 구독/값 누적 후보 |
| Charm_FlameSwordReturn | 1 | Charm_StatusInstance | 능력치 쓰기 |
| Charm_FlameWeapon | 1 | Charm_StatusInstance | 부모/개별 메서드 확인 |
| Charm_FollowerAttackSpeed | 1 | Charm_StatusInstance | 능력치 쓰기, 이벤트 구독/값 누적 후보 |
| Charm_Freeze | 1 | Charm_StatusInstance | 부모/개별 메서드 확인 |
| Charm_FreezeNormalSlash | 1 | Charm_Basic, IAttackableCharm | 능력치 읽기, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_FrostiumRing | 3 | Charm_Basic, IAttackableCharm | 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_FrozenEgg | 1 | Charm_StatusInstance | 이벤트 구독/값 누적 후보 |
| Charm_FrozenMemory | 1 | Charm_Basic | 부모/개별 메서드 확인 |
| Charm_GainBuffOnMPLoss | 1 | Charm_Basic | 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_GainShieldWhileUsingMP | 1 | Charm_Basic | 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_GreenGi | 1 | Charm_StatusInstance, IAttackableCharm | 능력치 읽기, 이벤트 구독/값 누적 후보 |
| Charm_GuardCounter | 2 | Charm_Basic, IAttackableCharm | 능력치 읽기, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_Guillotine | 1 | Charm_Basic, IAttackableCharm | 능력치 읽기, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_HitInvincible | 1 | Charm_Basic | 이벤트 구독/값 누적 후보 |
| Charm_HomingMagic | 1 | Charm_StatusInstance | 부모/개별 메서드 확인 |
| Charm_IceBat | 1 | Charm_StatusInstance, IAttackableCharm | 능력치 읽기, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_IceBow | 1 | Charm_Active, IAttackableCharm | 능력치 읽기, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_IceHammer | 1 | Charm_StatusInstance, IAttackableCharm | 능력치 읽기, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_IceSpear | 1 | Charm_Basic, IAttackableCharm | 능력치 읽기, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_IceSword | 1 | Charm_StatusInstance, IAttackableCharm | 능력치 읽기, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_IceWings | 1 | Charm_StatusInstance | 능력치 읽기, 이벤트 구독/값 누적 후보 |
| Charm_IncreaseAllDamage | 1 | Charm_StatusInstance | 부모/개별 메서드 확인 |
| Charm_IncreaseAllDamageByHP | 1 | Charm_Basic | 능력치 읽기, 능력치 쓰기, 이벤트 구독/값 누적 후보 |
| Charm_IncreaseAttackSpeed | 1 | Charm_Basic | 능력치 쓰기 |
| Charm_IncreaseCriticalChance_NormalAttack | 1 | Charm_Basic | 이벤트 구독/값 누적 후보 |
| Charm_IncreaseGoldDropRate | 1 | Charm_Basic | 능력치 쓰기 |
| Charm_IncreaseHP | 1 | Charm_Basic | 부모/개별 메서드 확인 |
| Charm_IncreaseLastAttackDamage_SwordAndShield | 1 | Charm_Basic | 이벤트 구독/값 누적 후보 |
| Charm_IncreaseMP | 1 | Charm_Basic | 부모/개별 메서드 확인 |
| Charm_IncreaseMPRegen | 1 | Charm_Basic | 능력치 쓰기 |
| Charm_IncreaseMeleeAttackRange | 1 | Charm_Basic | 능력치 쓰기 |
| Charm_IncreaseMoveSpeed | 4 | Charm_Basic | 부모/개별 메서드 확인 |
| Charm_IncreaseMpRegenOnGuard | 1 | Charm_Basic | 능력치 쓰기, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_KatanaEnhancedDashAttackActivator | 1 | Charm_Basic | 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_KillHeal | 1 | Charm_Basic | 부모/개별 메서드 확인 |
| Charm_KirinHorn | 1 | Charm_StatusInstance | 이벤트 구독/값 누적 후보 |
| Charm_Kunai | 1 | Charm_Basic, IAttackableCharm | 능력치 읽기, 주기 갱신 |
| Charm_LakeSpirit | 1 | Charm_Active, IAttackableCharm | 능력치 읽기, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_Lantern | 1 | Charm_Basic | 부모/개별 메서드 확인 |
| Charm_LeadNPC | 9 | Charm_Basic, ICompanionCharm, IAttackableCharm | 능력치 읽기, 능력치 쓰기, 연결/변형, 주기 갱신 |
| Charm_LightningBread | 1 | Charm_StatusInstance | 가방 조회 |
| Charm_LightningPouch | 1 | Charm_StatusInstance | 가방 조회, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_Lightning_BasicAttack | 1 | Charm_StatusInstance | 가방 조회, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_Lightning_Range | 1 | Charm_StatusInstance | 부모/개별 메서드 확인 |
| Charm_MPMultipleCast | 1 | Charm_StatusInstance | 능력치 읽기, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_MPShieldActive | 1 | Charm_Active | 능력치 쓰기, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_Magic | 26 | Charm_Basic, IMagicCharm, IAttackableCharm | 능력치 읽기, 연결/변형, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_MagicianCoin | 1 | Charm_StatusInstance | 가방 조회 |
| Charm_MagmaBead | 1 | Charm_StatusInstance | 부모/개별 메서드 확인 |
| Charm_MinHPKill | 1 | Charm_Basic | 능력치 읽기, 이벤트 구독/값 누적 후보 |
| Charm_MiniBallista | 1 | Charm_Basic, ICompanionCharm, IAttackableCharm | 가방 조회, 능력치 읽기, 연결/변형, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_MiniBossFight | 1 | Charm_Basic | 능력치 쓰기, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_MpHeal | 1 | Charm_Active | 능력치 읽기, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_NearLevelDamage | 1 | Charm_StatusInstance | 가방 조회, 능력치 쓰기, 이벤트 구독/값 누적 후보 |
| Charm_PallasCard | 1 | Charm_Basic, IAttackableCharm | 능력치 읽기, 주기 갱신 |
| Charm_PlanetComet | 1 | Charm_StatusInstance | 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_PlanetModule | 1 | Charm_StatusInstance | 가방 조회, 연결/변형 |
| Charm_PointedBat | 1 | Charm_StatusInstance | 이벤트 구독/값 누적 후보 |
| Charm_QuickCast | 1 | Charm_StatusInstance | 이벤트 구독/값 누적 후보 |
| Charm_Reddew | 1 | Charm_StatusInstance, IAttackableCharm | 능력치 읽기, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_ReduceMPCost | 1 | Charm_Basic | 가방 조회, 능력치 쓰기, 연결/변형, 이벤트 구독/값 누적 후보 |
| Charm_ReservedMPBonus | 1 | Charm_Basic | 능력치 쓰기 |
| Charm_RightSpellCooldownHelper | 1 | Charm_Basic | 가방 조회, 연결/변형, 이벤트 구독/값 누적 후보 |
| Charm_RockElephant | 1 | Charm_Active, IAttackableCharm | 능력치 읽기, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_ScytheOfBerut | 1 | Charm_StatusInstance | 능력치 쓰기 |
| Charm_ShadowEye | 1 | Charm_StatusInstance, IAttackableCharm | 능력치 읽기, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_Shieldmate | 1 | Charm_StatusInstance | 능력치 쓰기 |
| Charm_ShortContract | 1 | Charm_Basic | 이벤트 구독/값 누적 후보 |
| Charm_SnowNeckless | 1 | Charm_StatusInstance | 부모/개별 메서드 확인 |
| Charm_SpeedRun | 1 | Charm_Basic | 능력치 쓰기, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_StatusInstance | 111 | Charm_Basic | 능력치 쓰기 |
| Charm_Stun | 1 | Charm_Basic | 능력치 읽기, 이벤트 구독/값 누적 후보 |
| Charm_SummonGreenBat | 7 | Charm_Basic, IAttackableCharm | 능력치 읽기, 연결/변형, 이벤트 구독/값 누적 후보 |
| Charm_SummonUnit | 5 | Charm_Basic, ICompanionCharm, IAttackableCharm | 능력치 읽기, 능력치 쓰기, 연결/변형, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_SweepRange | 2 | Charm_Basic | 가방 조회, 능력치 쓰기, 주기 갱신 |
| Charm_SwordOfLight | 1 | Charm_StatusInstance | 이벤트 구독/값 누적 후보 |
| Charm_TheFlagOfCheer | 1 | Charm_Active | 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_TheTyphoonSheetmusic | 1 | Charm_StatusInstance, IAttackableCharm | 능력치 쓰기, 이벤트 구독/값 누적 후보 |
| Charm_ThunderousSteps | 1 | Charm_Active | 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_TooCloseDamage | 1 | Charm_Basic | 이벤트 구독/값 누적 후보 |
| Charm_TradeMoney | 1 | Charm_StatusInstance | 능력치 읽기, 이벤트 구독/값 누적 후보 |
| Charm_TuningForks | 1 | Charm_Basic, IAttackableCharm | 능력치 읽기, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_UpCharmDamage | 2 | Charm_Basic, IDependencyConditionCharm | 가방 조회, 동적 카테고리, 연결/변형, 이벤트 구독/값 누적 후보 |
| Charm_WarmGlove | 1 | Charm_Basic | 이벤트 구독/값 누적 후보 |
| Charm_WarmStone | 1 | Charm_Basic | 능력치 쓰기 |
| Charm_WaterBag | 1 | Charm_Basic | 능력치 쓰기, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_WhitePaper | 1 | Charm_StatusInstance | 가방 조회, 동적 카테고리 |
| Charm_Wings | 1 | Charm_StatusInstance | 능력치 읽기, 능력치 쓰기, 이벤트 구독/값 누적 후보, 주기 갱신 |
| Charm_WoodenBox | 1 | Charm_Basic | 가방 조회, 능력치 쓰기, 이벤트 구독/값 누적 후보 |

## 석판 전체

| 엔티티 | 이름 | 분류 | 쿼리 | 조건 |
|---|---|---|---|---|
| 2000 | 억압 | 정적 쿼리 | RIGHT X |  |
| 2001 | 희망 | 정적 쿼리 | RIGHT 1 |  |
| 2002 | 고양 | 정적 쿼리 | RIGHT IGNORECRITERIA |  |
| 2003 | 기적 | 정적 쿼리 | HORIZONTAL 1 VERTICAL 1 |  |
| 2004 | 전이 | 정적 쿼리 | HORIZONTAL +1 VERTICAL -1 |  |
| 2005 | 착취 | 정적 쿼리 | RIGHT 1 LEFT -1 |  |
| 2006 | 응집 | 정적 쿼리 | HORIZONTAL -1 UP +3 |  |
| 2007 | 선의 | 정적 쿼리 | RIGHT 1 LEFT 1 | BOTTOM PLACED |
| 2008 | 정의 | 정적 쿼리 | VERTICAL +1 | LEFTEND PLACED RIGHTEND PLACED |
| 2009 | 화합 | 정적 쿼리 | UP -1 RIGHT +1 LEFT -1 DOWN +1 |  |
| 2010 | 운명 | 정적 쿼리 | DOWN +1 |  |
| 2011 | 기사도 | 정적 쿼리 | KNIGHTUPLEFT 1  |  |
| 2012 | 재치 | 정적 쿼리 | DIAUPLEFT 1 |  |
| 2013 | 시선 | 정적 쿼리 | DIAUPLEFT 1 DIADOWNRIGHT -1 |  |
| 2014 | 고동 | 정적 쿼리 | UPUP 2 |  |
| 2015 | 양육 | 정적 쿼리 | DIAUPLEFT 1 DIAUPRIGHT 1 UP 1 DOWN -1 DOWNDOWN -1 |  |
| 2016 | 차양 | 정적 쿼리 | BOTTOM 1  | TOP PLACED |
| 2017 | 쌍성 | 정적 쿼리 | UPUP 2 DOWNDOWN 2 |  |
| 2018 | 이음 | 정적 쿼리 | UP 2 DOWN IGNORECRITERIA |  |
| 2019 | 경쟁 | 정적 쿼리 | DIAUPLEFT -1 UP -1 DOWN +3 |  |
| 2020 | 열망 | 정적 쿼리 | UP 2 |  |
| 2021 | 도래 | 정적 쿼리 | UP 1 UPUP 1 DOWN -1 DOWNDOWN -1 |  |
| 2022 | 경계 | 정적 쿼리 | TOP 1 BOTTOM 1 |  |
| 2023 | 파도 | 정적 쿼리 | DIAUPRIGHT 3 UP -1 RIGHT -1 |  |
| 2024 | 장난 | 정적 쿼리 | UP 1 DIAUPLEFT 1 DIAUPRIGHT 1 LEFT -1 RIGHT -1 |  |
| 2025 | 헌정 | 정적 쿼리 | DIAUPLEFT 1 DIAUPRIGHT 1 DIADOWNLEFT 1 DIADOWNRIGHT 1 |  |
| 2026 | 환호 | 정적 쿼리 | UP 1 |  |
| 2027 | 수확 | 정적 쿼리 | UP 2 DOWN 2 |  |
| 2028 | 전진 | 정적 쿼리 | UP 1 UPUP 1 UPUPUP 1 |  |
| 2029 | 기반 | 정적 쿼리 | HORIZONTAL 1 |  |
| 2030 | 분배 | 정적 쿼리 | UP 1 DOWN 1 LEFT 1 RIGHT 1 |  |
| 2031 | 준비 | 정적 쿼리 | DIADOWNRIGHT 2 DIAUPLEFT 1 |  |
| 2032 | 입구 | 정적 쿼리 | DIAUPRIGHT 1 DIAUPLEFT 1 UP 2 |  |
| 2033 | 백일몽 | 정적 쿼리 | DIAUPRIGHT 2 DIAUPLEFT 2 DIADOWNRIGHT 2 DIADOWNLEFT 2 |  |
| 2034 | 맹세 | 정적 쿼리 | LEFT 1 RIGHT 1 DOWN 1 UP 1 UPUP 2 |  |
| 2035 | 가시 | 정적 쿼리 | LEFT 1 RIGHT 1 DOWN 2 UP 2 DIAUPRIGHT 1 DIAUPLEFT 1 DIADOWNRIGHT 1 DIADOWNLEFT 1 |  |
| 2036 | 접합 | 정적 쿼리 | UP 1 UPUP 1 UPUPUP 1 RIGHT 1 RIGHTRIGHT 1 RIGHTRIGHTRIGHT 1 |  |
| 2037 | 반항 | 정적 쿼리 | RIGHT_RISING 1 LEFT_FALLING 1 |  |
| 2038 | 삼두 | 정적 쿼리 | UP 1 LEFT 1 RIGHT 1 |  |
| 2039 | 과거 | 정적 쿼리 | UP 1 RIGHT 1 DIAUPLEFT 1 DIAUPRIGHT 1 |  |
| 2040 | 미래 | 정적 쿼리 | UP 1 LEFT 1 DIAUPLEFT 1 DIAUPRIGHT 1 |  |
| 2041 | 적재 | 정적 쿼리 | UP 1 DIAUPLEFT 1 KNIGHTUPLEFT 1 UPUP 1 |  |
| 2042 | 광휘 | 정적 쿼리 | HORIZONTAL 1 UP 2 DOWN 2 |  |
| 2043 | 악수 | 정적 쿼리 | UP 1 DOWN 1 |  |
| 2044 | 근사 | 정적 쿼리 | UP 1 RIGHT 1 |  |
| 2045 | 권능 | 정적 쿼리 | UP 3 |  |
| 2046 | 압축 | 정적 쿼리 | UP 3 UPUP 2 UPUPUP 1 |  |
| 2047 | 건조 | 정적 쿼리 | DOWN 1 UP 1 |  |
| 2048 | 동시성 | 정적 쿼리 | VERTICAL 1 |  |
| 2049 | 단절 | 정적 쿼리 | UP 3 DOWN 3 LEFT -1 RIGHT -1 |  |
| 2050 | 출구 | 정적 쿼리 | DIADOWNLEFT 1 DOWN 2 DIADOWNRIGHT 1 |  |
| 2051 | 확신 | 정적 쿼리 | UP 5 |  |
| 2052 | 집결 | 정적 쿼리 | UP 2 LEFT 2 |  |
| 2053 | 발전 | 정적 쿼리 | DIAUPLEFT 2 UP 1 LEFT 1 |  |
| 2054 | 평화 | 정적 쿼리 | LEFT 3 RIGHT 3  |  |
| 2055 | 용기 | 정적 쿼리 | LEFT_RISING 1 RIGHT_FALLING 1 DIAUPRIGHT 2 DIADOWNLEFT 2 |  |
| 2056 | 환대 | 정적 쿼리 | LEFT 1 UP 2 UP IGNORECRITERIA LEFT IGNORECRITERIA |  |
| 2057 | 명예 | 정적 쿼리 | KNIGHTUPLEFT 1 UP 2 |  |
| 2058 | 배수진 | 정적 쿼리 | UP 5 DOWN -1 LEFT -1 RIGHT -1 |  |
| 2059 | 깃발 | 정적 쿼리 | UP 1 DOWN -1 RIGHT 1 RIGHTRIGHT 2 RIGHTRIGHTRIGHT 3 | LEFTEND PLACED |
| 2060 | 방어수 | 정적 쿼리 | DIAUPLEFT 1 DIAUPRIGHT 2 DIADOWNLEFT 2 DIADOWNRIGHT 1 LEFT -1 RIGHT -1 |  |
| 2061 | 쐐기 | 정적 쿼리 | DIAUPLEFT 3 |  |
| 2098 | 지옥 | 정적 쿼리 | CHECKERBOARD2 -9 CHECKERBOARD -9 |  |
| 2099 | 천국 | 정적 쿼리 | CHECKERBOARD2 9 CHECKERBOARD 9 |  |
| 2100 | 보은 | 정적 쿼리 | HORIZONTAL 3 |  |
| 2101 | ... | 인스턴스 쿼리 필요 |  |  |
| 12000 | 저주 | 정적 쿼리 | CHECKERBOARD2 1 CHECKERBOARD -1 |  |
| 12002 | 12002 | 정적 쿼리 | O MUL/2 |  |

## 콤보 전체

현재 점수는 콤보별 실제 효과량 대신 공통 발동·진행 가치를 쓴다. 아래 단계는 저장 카탈로그 값이며 특수 클래스의 단계 누락 가능성이 있다.

| ID | 이름 | 저장된 단계 |
|---|---|---|
| ACADEMY | 아카데미 | 2, 4, 6, 8, 10 |
| ALCHEMY | 연금술 | 1, 2 |
| COMPANION | 동료 | 2, 3, 4, 5, 6, 8, 10 |
| CURSE | 저주 | 2, 4 |
| DARKCLOUD | 먹구름 | 2, 3, 4, 5, 6, 8, 10 |
| ELEMENTAL | 원소 | 2, 4, 6 |
| EMBER | 잉걸불 | 2, 3, 4, 5, 6, 8, 10 |
| FLAMESWORD | 태양검 | 4, 6, 8, 10 |
| FROST | 얼음무구 | 2, 3, 4, 5, 6, 8, 10 |
| GLACIER | 빙하 | 2, 3, 4, 5, 6, 8, 10 |
| GUARDIAN | 수호 | 2, 3, 4, 5, 6, 8, 10 |
| LAKE | 호수 | 3, 6, 9 |
| MAGITECH | 마법공학 | 2, 3, 4, 5, 6, 8, 10 |
| MYSTIC | 신비 | 2, 3, 4, 5, 6 |
| PLANET | 행성 | 2, 3, 4, 5, 6, 8, 10 |
| PRECISION | 정밀 | 2, 3, 4, 5, 6, 8, 10 |
| SAVVY | 교섭 | 2, 4 |
| SHADOW | 그림자 | 2, 3, 4, 5, 6, 8, 10 |
| STURDY | 견고 | 2, 3, 4, 5, 6, 8, 10 |
| WINDSONG | 바람노래 | 2, 3, 4, 5, 6, 8, 10 |

## 현재 어셈블리의 추가 동작 타입

저장 카탈로그에 연결되지 않은 타입이다. 부모·미사용·비활성 타입이 섞이므로 새 아이템이나 누락 아이템으로 단정하지 않는다.

- `Charm_Active`
- `Charm_AddMaxRage`
- `Charm_AttackBuffOnHealthLoss`
- `Charm_AttackSummon`
- `Charm_AutoMagic`
- `Charm_Basic`
- `Charm_BasicLightning`
- `Charm_BoltMagicAmmoBonus`
- `Charm_BoltMagicAmmoBonus_HitTarget`
- `Charm_BurnExplosion`
- `Charm_ChangeATKDMGToAPDMG`
- `Charm_CrossbowDashAmmo`
- `Charm_DashAttackInvincible`
- `Charm_DebuffToBuff`
- `Charm_DecreaseFallingDamage`
- `Charm_ExplosionDamageResist`
- `Charm_FireBulletOnHit`
- `Charm_FireBulletOnKill`
- `Charm_FireDamageGround`
- `Charm_FireGuard`
- `Charm_FlameBall`
- `Charm_FlameDash`
- `Charm_FlameGroundKillCrit`
- `Charm_FullChargeMagicAPBonus`
- `Charm_GainMPByDamage`
- `Charm_GoldIsDamage`
- `Charm_Golem`
- `Charm_GolemPart`
- `Charm_Golem_Gun`
- `Charm_Golem_Laser`
- `Charm_GrowthGuard`
- `Charm_GrowthKatana`
- `Charm_GrowthMoveSpeed`
- `Charm_GrowthParry`
- `Charm_GrowthStatusInstance`
- `Charm_GuardBuff`
- `Charm_GuardSmite`
- `Charm_IceBat_Projectile`
- `Charm_IncreaseAP`
- `Charm_IncreaseBasicAttackDamage`
- `Charm_IncreaseCooldownRecoverySpeed`
- `Charm_IncreaseDirectAttackExternalForce`
- `Charm_IncreasePeacefulMoveSpeed`
- `Charm_IncreasePerfectGuardTime`
- `Charm_IncreaseShieldDefenseRange`
- `Charm_IncreaseShieldDurability`
- `Charm_IncreaseWeaponDamageFromGuardGauge`
- `Charm_KillMaxHP`
- `Charm_Lightning_DarkCloudBuff`
- `Charm_Lightning_Faster`
- `Charm_Lightning_PerfectGuard`
- `Charm_MagicCoolDownBonusByTag`
- `Charm_MagicDamageBySpeed`
- `Charm_MagicDash`
- `Charm_MinimumLightning`
- `Charm_MovingCast`
- `Charm_NearMagicBullet`
- `Charm_NotUseBoltMagicAmmo`
- `Charm_PerfectGuardDamageQuest`
- `Charm_RageGain`
- `Charm_Rapier`
- `Charm_ShieldBash`
- `Charm_ShieldDamageBonus`
- `Charm_StatusDebuff`
- `Charm_SubShield`
- `Charm_SummonRedPlanet`
- `Charm_SwordDamage`
- `Charm_VenomSporePouch`
- `Charm_WeaponSkill`

## 활성 조건 및 특수 콤보 타입

- `CharmActivateCriteria_BothSideCharm`
- `CharmActivateCriteria_BothSidesAreEmpty`
- `CharmActivateCriteria_BottomInInventory`
- `CharmActivateCriteria_FullHP`
- `CharmActivateCriteria_Inside`
- `CharmActivateCriteria_Near8MagicBook`
- `CharmActivateCriteria_NeighborsAreFull`
- `CharmActivateCriteria_Outlined`
- `CharmActivateCriteria_SideEnd`
- `CharmActivateCriteria_TopInInventory`
- `ComboEffect_Alchemy`
- `ComboEffect_DarkCloud`
- `ComboEffect_Debuff`
- `ComboEffect_FlameSword`
- `ComboEffect_Frost`
- `ComboEffect_Guardian`
- `ComboEffect_Mystic`
- `ComboEffect_Planet`
