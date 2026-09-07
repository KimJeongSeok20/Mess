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

> 할당량은 코드에서 `Tax`로 불립니다. 회사가 팀에게 청구하는 상납금이라는 뜻이며 실제 세금과는 무관합니다.

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

## 주요 구현

### 1. 클라이언트 신뢰 구조를 서버 권한으로 옮김

**문제.** 초기에는 인벤토리·거래·부활을 클라이언트가 결정하고 서버에 통보했습니다. 아이템 복제와 무료 부활이 그냥 가능했습니다.

**접근.**
- 서버가 플레이어별 [`ServerInventoryLedger`](src/Assets/Scripts/Inventory/ServerInventoryLedger.cs)를 들고, 모든 아이템은 서버가 발급한 토큰으로만 식별됩니다.
- 거래·드롭·강화·재장전·유료 부활은 전부 `[ServerRpc(requireOwnership: false)]`로 들어와서 토큰 소유권을 확인한 뒤에만 상태를 바꿉니다.
- 클라이언트는 결과를 `TargetRpc`/`ObserversRpc`로 돌려받아 연출만 합니다.

**검증.** [`ServerInventoryTransactionTests.cs`](src/Assets/Editor/ServerInventoryTransactionTests.cs), [`DeathLifecycleRegressionTests.cs`](src/Assets/Editor/DeathLifecycleRegressionTests.cs)로 위조 토큰·중복 소비·잘못된 부활 요청이 거부되는지 확인합니다.

### 2. 절차 생성 던전에 베이크 조명 쓰기

**문제.** 던전이 런타임에 조립되므로 라이트맵을 미리 구울 수 없고, 전부 실시간 조명으로 돌리면 프레임이 나오지 않습니다. 방마다 전원(P0/P100)도 바뀌어야 합니다.

**접근.**
- 방 프리팹을 회전 4종 × 전원 2종으로 미리 굽고([`DungeonTileBakeData`](src/Assets/Scripts/Dungeon%201/Lighting/DungeonTileBakeData.cs), [`DungeonTilePowerBakeSet`](src/Assets/Scripts/Dungeon%201/Lighting/DungeonTilePowerBakeSet.cs)), 생성 후 타일마다 라이트맵 슬롯을 다시 붙입니다.
- 문 양쪽의 조명 차이는 [`DungeonDoorDualSideProbeReceiver`](src/Assets/Scripts/Dungeon%201/Lighting/DungeonDoorDualSideProbeReceiver.cs)가 프로브를 나눠 받아 처리합니다.
- 인접 방 사이 빛 전달은 방 쌍마다 굽지 않고, 방별 "나가는 빛"만 캡처해 런타임에 합성하는 방식([`Experiments/DungeonRoomLocalLightShare`](src/Assets/Experiments/DungeonRoomLocalLightShare))을 택했습니다. 그 전에 시도한 인접 라이팅·포털 트랜스포트·베이크 기저 PoC 세 갈래도 [`Experiments/`](src/Assets/Experiments)에 그대로 남겨 두었습니다.

**결과.** 라이트 셰어 카탈로그에 방 8개가 빠져 생성 중 한 프레임이 4.6초 걸리던 문제를 찾아 고친 뒤, 던전 생성 8.4초 → 2.4초, 40ms 초과 프레임 96 → 15개(에디터 호스트 기준).

<!-- 어떤 PoC가 왜 실패했는지 2~3줄 더 쓰면 좋습니다. 면접에서 가장 많이 물어볼 지점입니다. -->

### 3. 로딩 시간

**문제.** 메인 메뉴 → 게임 씬 로딩이 HDD 빌드에서 81.5초. 씬이 던전 타일 프리팹 전체를 참조해 텍스처 5.25GB가 한 번에 올라왔습니다.

**접근.** 로그 기반 타이밍 계측을 붙여 구간을 나눈 뒤, 밉맵 스트리밍을 전역으로 켜고 몬스터·소품 4K 텍스처를 2K로 캡했습니다. 라이트맵은 스트리밍하면 낮은 밉을 샘플링해 바닥에 이음새가 생겨서 제외했습니다.

**결과.** 81.5초 → 58.5초, 빌드 6.47GB → 5.97GB. 다음 단계는 타일 프리팹의 Addressables 분리입니다.

### 4. 몬스터 AI

| 몬스터 | 방식 | 특징 |
|---|---|---|
| Clown | Unity Behavior Graph | 타깃 획득 → 접근 → 달리며 선물 투척. 그래프를 [에디터 스크립트](src/Assets/Monster/Clown/Scripts/Editor/ClownBehaviorGraphBuilder.cs)로 조립해 재현 가능하게 유지 |
| smily | 커스텀 상태 머신 ([`SmilyBrain`](src/Assets/Scripts/monster/SmilyBrain.cs)) | 시야·거리 감지, 점프 포인트 이동, 벽 간격 보정 |
| Octopus | 스웜 컨트롤러 ([`OctopusSwarmController`](src/Assets/Scripts/monster/Octopus/OctopusSwarmController.cs)) | 컨트롤러 하나가 멤버 여러 마리를 구동 |

공통으로 [`DoorAutoOpener`](src/Assets/Scripts/monster/DoorAutoOpener.cs)가 NavMesh 경로가 실제로 문을 통과할 때만 문을 열고, [`CorpseProcessor`](src/Assets/Scripts/Currency/CorpseProcessor.cs)가 사망 시 시체와 드롭을 처리합니다.

### 5. 에디터 자동화와 회귀 테스트

- [`Editor/`](src/Assets/Editor) 79개 중 19개가 테스트입니다. 전투·사망·인벤토리·할당량·스킬·Steam 방 계약이 깨지면 바로 잡힙니다.
- 라이트맵 회전 베이크, 방 프리팹 정규화, 문 개구부 검증, NavMesh 영역 저작을 `Tools > Dungeon V2 > Control Center` 하나로 모았습니다.
- [`DebugRemoteControl`](src/Assets/Scripts/Debug/DebugRemoteControl.cs)로 Play Mode의 플레이어 이동·던전 생성·날씨를 CLI에서 조작해 재현 테스트를 돌립니다.

---

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

## 크레딧

- smily 몬스터 모델 — CC 저작자 표시 (`smily-horror-monster`)
<!-- 빌드에 포함된 다른 서드파티 에셋의 저작자 표기가 필요하면 여기에 추가하세요. -->
