# Mess

**팀으로 절차 생성 던전을 털어 3일마다 회사 할당량을 채우고, 못 채우면 계약이 끊기는 협동 공포 게임.**
Unity 6 · URP · PurrNet 멀티플레이 · 1인 개발

<!-- 게임플레이 GIF 또는 스크린샷 2~3장. README에서 가장 먼저 읽히는 자리입니다. -->

| | |
|---|---|
| **플레이 빌드** | [Releases](https://github.com/KimJeongSeok20/Mess/releases/latest) |
| **플레이 영상** | <!-- YouTube 링크 --> |
| **개발 기간** | <!-- 예: 2026.03 ~ 진행 중 --> |
| **역할** | 기획 · 프로그래밍 · 툴링 전담 (1인) |
| **저장소 성격** | 코드 리뷰용. 직접 작성한 C# 473개만 포함, **빌드 불가** ([이유](#이-저장소에-대하여)) |

---

## 5분 코드 투어

시간이 없다면 이 다섯 파일만 보셔도 됩니다.

| 보고 싶은 것 | 파일 | 한 줄 요약 |
|---|---|---|
| 서버 권한 인벤토리 | [`ServerInventoryLedger.cs`](src/Assets/Scripts/Inventory/ServerInventoryLedger.cs) · [`NetworkPlayerInventory.cs`](src/Assets/Scripts/Network_Integration/NetworkPlayerInventory.cs) | 서버가 아이템 토큰 장부를 갖고, 클라이언트는 요청만 보냄 |
| 할당량·계약 해지 규칙 | [`TaxSchedule.cs`](src/Assets/Scripts/Tax/TaxSchedule.cs) · [`TaxScheduleTests.cs`](src/Assets/Scripts/Tax/Editor/TaxScheduleTests.cs) | 순수 함수로 분리해 경계 조건을 테스트 |
| 하루 사이클 동기화 | [`TimeManager.cs`](src/Assets/Scripts/TimeSync/TimeManager.cs) | SyncVar 시계 + 침대 착석 투표 + 던전 시드 확정 |
| 절차 던전의 베이크 조명 | [`DungeonTileLightmapSwitcher.cs`](src/Assets/Scripts/Dungeon%201/Lighting/DungeonTileLightmapSwitcher.cs) · [`NetworkDungeonController.cs`](src/Assets/Scripts/Dungeon%201/NetworkDungeonController.cs) | 타일별 P0/P100 라이트맵 세트를 런타임에 교체 |
| 몬스터 AI | [`ClownBehaviorGraphBuilder.cs`](src/Assets/Monster/Clown/Scripts/Editor/ClownBehaviorGraphBuilder.cs) · [`DoorAutoOpener.cs`](src/Assets/Scripts/monster/DoorAutoOpener.cs) | 코드로 조립한 Behavior Graph, 경로가 문을 지날 때만 여는 문 통과 |

---

## 게임 소개

Lethal Company 계열의 협동 루팅 게임입니다. 하루는 게임 내 9:00~24:00, 실시간 10분입니다.

```
 시계 홀드 ──▶ 던전 생성 ──▶ 탐색·루팅 ──▶ 포장·판매 ──▶ 침대 투표 ──▶ 다음 날
   (서버 시드)   (DunGen)    (밤엔 몬스터↑)   (공유 자금)    (전원 동의)
                                                    │
                                          3일마다 할당량 청구 ──▶ 미달 시 계약 해지 (1일차부터 재시작)
```

| 규칙 | 값 | 근거 |
|---|---|---|
| 할당량 | 3일마다 2,500원, 사이클마다 +30% | `TaxSchedule.DefaultTaxPerCycle`, `DefaultGrowthPerCycle` |
| 계약 해지 | 마감일까지 미달이면 공유 자금 0, 1일차로 리셋 | `TaxCollectionMachine.evictOnUnpaidTax` |
| 몬스터 스폰 | 낮 45초 간격 · 최대 2마리 → 밤 12초 간격 · 최대 8마리 | `DungeonMonsterSpawner` |
| 사망 | 유령 상태로 대기, 팀원이 리셉션에서 돈을 내야 부활. 부활할수록 비쌈 | `ReviveStation`, `ReviveLedger` |
| 판매 | 손에 든 아이템 → GiftBox 포장 → Mailbox 투입 → 공유 자금 | `GiftBox`, `Mailbox`, `CurrencyManager` |

여기에 모루 강화, 스킬 터미널(퍽), 무기 상점, 가방 확장, 방 단위 전원 차단이 붙습니다.

---

## 기술 스택

| 분야 | 기술 | 이 프로젝트에서 한 일 |
|---|---|---|
| 엔진 | Unity 6 (6000.4.1f1), C#, URP 17.4 | 렌더 피처 작성(상호작용 아웃라인), 셰이더 변종 스트리핑, 라이트맵 슬롯 런타임 교체 |
| 네트워킹 | PurrNet, Steamworks.NET | ServerRpc/ObserversRpc/SyncVar로 서버 권한 인벤토리·시간·전원·문 상태 동기화. UDP와 Steam P2P 트랜스포트 전환, Steam 로비 |
| 절차 생성 | DunGen | 포스트프로세서 4종으로 문·전리품·몬스터·조명을 생성 직후 주입. 서버 시드 → 클라이언트 동일 결과 |
| AI | Unity Behavior (Behavior Graph), AI Navigation (NavMesh) | Clown용 커스텀 Action/Condition 노드, 런타임 NavMesh 파이프라인, 문 통과 링크 |
| FPS·애니메이션 | Kinemation FPS Animation Framework, Input System | 무기 상태와 인벤토리 브리지, 재장전·탄약 서버 검증, 점프·질주 안정화 저작 도구 |
| 테스트 | Unity Test Framework (NUnit) | Edit Mode 회귀 테스트 21개 파일, 스탠드얼론 검증 빌드, CLI 원격 조작 |
| 도구 | Git, ParrelSync, Unity CLI · MCP, Claude Code | 2인 동시 접속 재현, 에디터 스크립트 원격 실행, AI 에이전트로 회귀 테스트·감사 문서 작성 후 직접 검증 |

## 코드 구조

```
src/Assets/
├── Scripts/            게임 로직 (193)
│   ├── Dungeon 1/        던전 생성·조명·전원·NavMesh   37
│   ├── Inventory/        인벤토리·아이템·무기          33
│   ├── monster/          몬스터 공통·smily·Octopus     31
│   ├── Player/           체력·사망·부활·퍽             12
│   ├── Menu/             메인 메뉴·세이브·Steam 방      9
│   ├── Tax/              할당량·계약·계약 해지          8
│   ├── Currency/         공유 자금·판매·시체            8
│   ├── Interaction/      문·리셉션·스킬 터미널          6
│   ├── TimeSync/         하루 사이클                     4
│   └── ...               Shop, Upgrade, Combat, Audio, UI, Debug
├── Experiments/        던전 조명 PoC (164)
├── Editor/             에디터 툴·회귀 테스트 (79)
├── Monster/            Clown Behavior Graph · smily 액션 (33)
└── Tests/              전투·카메라 스모크 (2)
```

---

## 이 저장소에 대하여

**클론해도 빌드되지 않습니다.** 에셋스토어에서 구매한 모델·사운드·에디터 확장(DunGen, PurrNet, Kinemation 등)이 프로젝트의 큰 부분을 차지하는데 라이선스상 재배포할 수 없어 제외했습니다. 씬·프리팹·머티리얼도 함께 빠졌습니다. 직접 작성한 C# 스크립트 473개만 들어 있습니다.

플레이는 [Releases](https://github.com/KimJeongSeok20/Mess/releases/latest)의 빌드로 해주세요.
