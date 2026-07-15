# Configuration Manager 두 구현 분석 및 비교

> 분석 기준: 2026-07-15 (Asia/Seoul)
> 분석 대상: 각 저장소의 기본 브랜치 `master`
> 방법: 전체 Git clone, 소스/프로젝트/manifest/이력 정적 분석, clean 환경 빌드 시도, 공식 저장소·배포 페이지 교차 확인

## 1. 저장 결과와 스냅샷

두 저장소는 `.git` 이력까지 포함한 non-shallow clone으로 `refer/` 아래에 보존했다. 이 문서의 줄 번호와 판단은 아래 커밋에 고정된다.

| 저장소 | 로컬 경로 | 기준 HEAD | 버전/최근 커밋 | 상태 |
|---|---|---|---|---|
| [AzumattDev/BepInEx.ConfigurationManager](https://github.com/AzumattDev/BepInEx.ConfigurationManager) | [`refer/AzumattDev-BepInEx.ConfigurationManager`](refer/AzumattDev-BepInEx.ConfigurationManager/) | `9bc0ce34ab58cf038762cd3dbbc3f908a583fa1b` | `18.4.1`, 2025-03-12 | `master`, 167 commits, tag 없음, clean |
| [shudnal/ConfigurationManager](https://github.com/shudnal/ConfigurationManager) | [`refer/shudnal-ConfigurationManager`](refer/shudnal-ConfigurationManager/) | `d7ec54716dcde2fbefdbc548652b0a07feb6cc5f` | `1.1.14`, 2026-07-15 | `master`, 97 commits, clean |

단순 physical line 집계 기준으로 Azumatt판은 tracked C# 17개/4,478줄, shudnal판은 21개/6,785줄이다. 주석과 빈 줄을 포함한 수치이므로 복잡도보다 대략적인 유지보수 표면을 나타낸다.

### 스냅샷 주의점

- Azumatt의 기본 `master`는 18.4.1이다. 원격 `Improvements` 브랜치는 18.4.3까지 진행됐고 Thunderstore에는 18.4.4가 배포되어 있으나, 공개 ref에서 18.4.4 대응 source commit은 확인되지 않았다. 따라서 이 문서는 **요청된 저장소의 기본 브랜치 소스**를 분석하며 18.4.4 바이너리를 분석한 문서가 아니다.
- shudnal의 HEAD, 플러그인 상수, committed Thunderstore manifest와 조사 시 Thunderstore 버전은 모두 1.1.14로 일치한다.

## 2. 결론부터

두 프로젝트는 같은 BepInEx Configuration Manager 계보를 공유하지만 제품 방향이 다르다.

- **Azumatt판**은 여러 BepInEx 게임에서 재사용할 수 있는 범용 기반, BepInEx 5 Mono와 BepInEx 6 IL2CPP 이중 타깃, plugin pin, 화면 밖 항목 생략과 pool/cache 같은 성능 최적화가 핵심이다.
- **shudnal판**은 Valheim 전용 통합, resizable Split View, 상세 편집 창, 강한 파일 편집기, 동적 readonly/browsable, 서버 동기화 상태와 정책 제어가 핵심이다.
- 범용 코드 기반이나 IL2CPP가 목적이면 Azumatt 쪽이 가깝다. Valheim modpack 운영과 server-controlled config UX가 목적이면 shudnal 쪽이 훨씬 완성도가 높다.
- 두 DLL을 함께 설치해 기능을 합치는 방식은 적절하지 않다. Azumatt는 원본과 같은 GUID/assembly identity를 사용하고, shudnal은 원본 GUID에 대한 incompatibility를 선언하며 동일한 `ConfigurationManager` assembly/namespace 역할을 차지한다.
- 어느 쪽도 현재 clone만으로 clean build되지 않았다. Azumatt는 저장소 안의 빌드 정의 결함, shudnal은 외부 Valheim/BepInEx/CCS assembly와 개발자 경로 결합이 주원인이다.

## 3. 코드 계보

```text
BepInEx/BepInEx.ConfigurationManager
├─ GitHub fork + 원본 Git 이력 보존 ──> AzumattDev/BepInEx.ConfigurationManager
└─ 2024년 새 Git 이력에 코드 반입 ──> shudnal/ConfigurationManager
                                      ├─ aedenthorn판의 coloring/localization 차용
                                      └─ Azumatt dev branch의 color drawer 차용
```

- GitHub는 Azumatt 저장소를 공식 원본의 fork로 표시한다. 원본 `master`와의 merge base는 `dc09cbd1...`이며 조사 시 Azumatt `master`는 원본 대비 ahead 24 / behind 6이었다.
- shudnal은 GitHub fork가 아니다. 2024-04-14의 새 root commit 뒤 소스를 한 번에 넣었고, README와 source header에서 원본 BepInEx, aedenthorn, Azumatt를 출처로 직접 명시한다.
- 두 clone의 전체 commit SHA 교집합은 0개다. 이는 공통 코드가 없다는 뜻이 아니라, shudnal이 원본 Git 이력을 보존하지 않은 snapshot-derived 계보라는 뜻이다.

정확한 표현은 **“Azumatt는 원본의 Git 포크, shudnal은 별도 Git 이력에서 원본/aedenthorn/Azumatt 코드를 명시적으로 반입해 Valheim 방향으로 발전한 파생판”**이다.

## 4. 핵심 비교표

| 비교 축 | AzumattDev판 | shudnal판 |
|---|---|---|
| 주 대상 | BepInEx 기반 Unity 게임 전반 | Valheim 전용 |
| 플러그인 ID | `com.bepis.bepinex.configurationmanager` | `_shudnal.ConfigurationManager` |
| 타깃 | BepInEx 5 Mono `net35` + BepInEx 6 IL2CPP `net6.0` | BepInEx 5 / .NET Framework 4.8 / Mono |
| 게임 결합 | 게임 assembly 직접 참조 없음 | Valheim 타입/publicized assembly/Harmony patch 직접 사용 |
| 기본 UI | 왼쪽 plugin/file + 오른쪽 상세의 2열 master-detail | 기본 Split View plugin/category tree + 상세, legacy List View 전환 |
| 창 | drag, 해상도 비율 기반 크기 | drag, 양방향 resize, 위치·크기·scale 기억, Valheim GUI scale 연동 |
| 목록 성능 | plugin·category viewport culling, view-model/texture pool과 cache | List View plugin culling, category auto-collapse, compact mode, file-tree cache |
| plugin pin | 있음, GUID를 config에 저장 | 없음 |
| 검색 | 1글자부터, space-token AND; plugin/GUID/name/category/설명/default/current | 2글자부터, 같은 AND 방식 + setting type 포함 |
| 필터 | normal/default/keybind/advanced/additional-info | advanced/keybind-only/read-only style |
| keybind 필터 타입 | `KeyboardShortcut`와 `KeyCode` | `KeyboardShortcut`만 |
| 값 drawer | bool, enum/flags, list/range, key, vector/quaternion, HSV/RGBA color, custom | 비슷한 기본형 + precision 개선, 실패 custom drawer fallback |
| 별도 상세 편집 창 | 없음 | 있음; staged Apply, list reorder, HSL/color/vector 상세 UI |
| config 파일 | 미인식 config의 inline raw 편집, save/open/delete | config/plugin tree, create/rename/save, trash 이동, JSON/YAML 검증 |
| reload | 명시적 `ConfigFile.Reload()` 없음 | 명시적 `ConfigFile.Reload()` 없음 |
| readonly | 정적 attribute, write 차단, read-only 표시 | Colored/Disabled/Hidden/Ignored + 매 frame/쓰기 직전 동적 재확인 |
| sync UX | 일반적인 read-only 표시 | CCS/ServerSync/Jotunn provider 표시, S/C 상태, 조건부 관리자 policy toggle |
| server hidden | 없음 | GUID=Section=Key 목록을 CCS로 동기화해 non-admin UI에서 숨김 |
| 입력 차단 | 범용 overlay canvas + `Input.ResetInputAxes()` | Valheim input/HUD/ZInput Harmony patch, Off/Player/All 정책 |
| pause/menu | 없음 | 선택적 pause, main/in-game menu button |
| localization | manager chrome이 영어 literal | UI 문구를 config로 노출하는 수동 config-driven localization |
| 배포 추적성 | GitHub release/tag 없음; Thunderstore 18.4.4와 공개 source ref 불일치 | committed manifest/HEAD/Thunderstore 1.1.14 일치; GitHub release는 `test`뿐 |
| 자동 테스트 | 없음 | 없음 |
| clean build | 실패: 절대경로 post-build + legacy analyzer path | 실패: 외부 game/BepInEx/CCS/Unity DLL 경로 부재 |

## 5. 공통 코어

두 구현은 다음 흐름을 공유한다.

1. BepInEx chainloader와 Unity object에서 plugin instance를 찾는다.
2. 각 plugin의 `ConfigFile`과 BepInEx core config를 `SettingEntryBase` 계층으로 감싼다.
3. `ConfigDescription`, acceptable list/range, 표준 ComponentModel attribute와 이름 기반 `ConfigurationManagerAttributes` tag를 UI metadata로 변환한다.
4. IMGUI에서 타입별 drawer를 선택하고 값 변경은 `ConfigEntryBase.BoxedValue`에 반영한다.
5. 기본 F1로 창을 열고 cursor를 unlock하며, public `DisplayingWindow`, event, `OverrideHotkey`, custom type drawer API를 제공한다.

실제 값 변경 시 두 구현 모두 owner config에 매번 `Config.Save()`를 직접 호출하지 않는다. BepInEx `ConfigFile.SaveOnConfigSet` 정책에 persistence를 맡긴다. 두 구현의 `BuildSettingList()`는 현재 memory의 entry를 다시 수집할 뿐 disk의 `ConfigFile.Reload()`가 아니다.

## 6. AzumattDev판 상세 분석

### 6.1 구조와 호환 범위

- [`ConfigurationManager/ConfigurationManager.csproj`](refer/AzumattDev-BepInEx.ConfigurationManager/ConfigurationManager/ConfigurationManager.csproj)는 `net35`, BepInEx 5.4.21, UnityEngine/InputSystem/UI package를 사용한다.
- [`ConfigurationManager.IL2CPP/ConfigurationManager.IL2CPP.csproj`](refer/AzumattDev-BepInEx.ConfigurationManager/ConfigurationManager.IL2CPP/ConfigurationManager.IL2CPP.csproj)는 `net6.0`, BepInEx 6 IL2CPP bleeding-edge와 HoneyCome 계열 IL2CPP Unity package를 사용한다.
- [`ConfigurationManager.Shared/ConfigurationManager.Shared.projitems`](refer/AzumattDev-BepInEx.ConfigurationManager/ConfigurationManager.Shared/ConfigurationManager.Shared.projitems)가 본체/UI/drawer/cache를 공유하고, Mono/IL2CPP별 차이는 주로 두 `SettingSearcher`에 격리한다.
- IL2CPP에서는 `BasePlugin`이 `MonoBehaviour`가 아니므로 proxy behaviour가 `Start/Update/LateUpdate/OnGUI/OnDestroy`를 전달한다. 근거: [`ConfigurationManager.cs`](refer/AzumattDev-BepInEx.ConfigurationManager/ConfigurationManager.Shared/ConfigurationManager.cs) L230-L247.

게임 고유 타입을 참조하지 않는 점은 분명한 장점이다. 다만 IL2CPP compile substrate가 특정 BepInEx bleeding-edge/HoneyCome package에 고정되어 있으므로 “모든 IL2CPP 게임에서 검증됨”으로 해석하면 안 된다.

### 6.2 UI와 탐색

- 왼쪽에 Plugins/Other Config Files, 오른쪽에 선택한 plugin setting 또는 raw file을 배치한다. 근거: main class L529-L816.
- plugin은 pin 여부와 이름으로 정렬하고, pin key인 GUID를 comma-separated config에 저장한다. 근거: L355-L380, L1267-L1298.
- 검색은 공백 token 모두가 결합 문자열에 포함되어야 하는 AND 검색이다. 검색 중에는 일반 필터를 우회해 advanced 항목도 찾는다. 근거: L322-L399.
- plugin list와 오른쪽 category 모두 이전 frame에서 측정한 높이를 이용해 viewport 밖 draw를 생략한다. 근거: L572-L616, L1031-L1108.
- `PluginSettingsDataPool`, `TexturePool`, style/drawer cache를 별도로 둔다. 대량 config에서 매 frame allocation과 IMGUI 비용을 줄이려는 설계가 가장 뚜렷한 구현이다.

### 6.3 값 편집과 파일 관리

- [`SettingFieldDrawer.cs`](refer/AzumattDev-BepInEx.ConfigurationManager/ConfigurationManager.Shared/SettingFieldDrawer.cs)는 bool, range/list, enum/flags, `KeyCode`, `KeyboardShortcut`, vectors, quaternion, 큰 HSV/RGBA color picker와 custom drawer를 제공한다.
- 별도 상세 편집 창은 없고 오른쪽 panel에서 즉시 적용한다.
- Mono의 [`SettingSearcher.cs`](refer/AzumattDev-BepInEx.ConfigurationManager/ConfigurationManager/SettingSearcher.cs) L79-L103은 plugin이 소유한 cfg를 제외한 config-root 아래 `.cfg/.json/.yaml/.yml`을 수집한다.
- raw editor는 읽은 내용을 memory에 cache한 뒤 `WriteAllText`로 저장하고, shell association으로 열거나 `File.Delete`로 즉시 삭제한다. parser 검증, backup/trash, atomic replace, unsaved-change/external-change 감지는 없다. 근거: main class L756-L846.

### 6.4 장점

- BepInEx 5 Mono와 6 IL2CPP를 같은 shared source로 관리한다.
- game-agnostic한 API 경계가 비교적 명확하다.
- pin, plugin website/log 열기, raw config 접근 등 많은 plugin을 다루는 사용자에게 편리하다.
- viewport culling과 object/texture cache가 코드로 실제 구현되어 있다.
- 배포물에 복사해 쓰는 [`ConfigurationManagerAttributes.cs`](refer/AzumattDev-BepInEx.ConfigurationManager/ConfigurationManagerAttributes.cs) template가 custom drawer/converter/order/read-only 등의 producer API를 문서화한다.

### 6.5 확인된 위험과 결함 후보

| 우선순위 | 항목 | 코드 근거/영향 |
|---|---|---|
| 높음 | IL2CPP Other Files 수집 오류 | IL2CPP `SettingSearcher.cs` L71-L102에서 recognized path 추가가 주석 처리되고 전체 수집이 `showDebug` 안에 있다. 기본 상태에서는 목록이 비고, 켜면 plugin cfg까지 “other”로 오분류할 수 있다. |
| 높음 | build portability 파손 | `Directory.Build.props` L63-L66이 모든 build 뒤 `C:\Users\crypt\...` Debug DLL을 무조건 복사한다. 실제 MSB3030을 재현했다. |
| 높음 | Mono analyzer 참조 혼재 | Mono csproj L50-L72가 `PackageReference`와 `..\packages\...` direct analyzer path를 함께 사용해 일반 `dotnet restore` 뒤 CS0006을 낸다. |
| 중간 | IMGUI event를 `Update()`에서 사용 | main class L1379-L1423이 `Event.current`와 GUI focus를 `OnGUI` 밖에서 읽는다. raw editor focus 시 invalid/null event 위험이 있다. |
| 중간 | debug enabled property가 반대로 readonly | `PropertySettingEntry.cs` L15-L20은 `ReadOnly = settingProp.CanWrite`다. write 가능한 `enabled` property를 차단하는 반전으로 보인다. |
| 중간 | color text 입력 흐름 | `SettingFieldDrawer.cs` L747-L810에서 hex/RGBA 입력이 뒤의 HSV 재조립에 덮이거나 반환값이 버려지는 경로가 있다. runtime 재현은 필요하다. |
| 중간 | panel 오류 은폐 | main class L790-L801의 broad catch가 log도 남기지 않아 drawer 오류 시 상세 panel이 조용히 빌 수 있다. |
| 중간 | raw file 안전성 | 검증/backup/trash 없이 덮어쓰기·즉시 삭제하며 cache reload가 없다. IL2CPP 오분류와 결합하면 영향이 커진다. |
| 낮음 | modifier shortcut 비대칭 | 열 때 전체 `KeyboardShortcut.IsUp()`, 닫을 때 main key만 검사한다(L432-L436, L1426-L1434). |
| 낮음 | localization 부재 | manager 자체 UI 문구가 영어 literal이고 locale layer/resource가 없다. |

## 7. shudnal판 상세 분석

### 7.1 구조와 Valheim 결합

- [`ConfigurationManager.cs`](refer/shudnal-ConfigurationManager/ConfigurationManager.cs) L18-L25에서 GUID `_shudnal.ConfigurationManager`, 버전 1.1.14, CCS 1.0.2 dependency와 원본 GUID incompatibility를 선언한다.
- [`ValheimConfigurationManager.cs`](refer/shudnal-ConfigurationManager/ValheimConfigurationManager.cs)는 player/HUD input, pause, menu button, GUI scale와 hidden settings를 담당한다.
- [`ConfigurationManager.csproj`](refer/shudnal-ConfigurationManager/ConfigurationManager.csproj)는 .NET Framework 4.8/C# 8이며 BepInEx, Harmony, Unity, publicized Valheim assembly와 ConditionalConfigSync를 직접 참조한다.
- Unity 6000 초기 임시 창을 피하는 `Display.main.systemWidth/Height` 처리와 최신 `Object.FindObjectsByType` 사용으로 현재 Valheim Unity API를 명확히 겨냥한다.

### 7.2 UI와 탐색

- 기본 Split View는 왼쪽 plugin/category tree와 오른쪽 setting을 분리하고, legacy List View로 전환할 수 있다. 근거: [`SettingsWindow.cs`](refer/shudnal-ConfigurationManager/SettingsWindow.cs) L190-L310, L350-L352.
- 창 position/size/scale을 config에 저장하고 drag/resize, double-click reset, Valheim GUI scale 연동을 지원한다. 근거: L45-L170.
- compact mode가 기본 켜져 있고 row/header/widget padding을 함께 줄인다.
- 2글자 이상 검색 시 plugin/GUID/name/category/description/default/type/current를 AND 검색한다. 검색 중 advanced/keybind-only 필터는 우회한다. 근거: L647-L730.
- category의 선언 순서를 기본 보존하고 선택적으로 이름순 정렬하며, category가 20개를 넘으면 default-only category를 자동 접는다. 근거: L670-L713.

### 7.3 두 가지 값 편집 모델

- [`SettingFieldDrawer.cs`](refer/shudnal-ConfigurationManager/SettingFieldDrawer.cs)는 메인 row에서 즉시 적용한다. custom drawer가 실패하면 setting 단위로 기록하고 표준 drawer로 fallback한다.
- [`SettingEditWindow.cs`](refer/shudnal-ConfigurationManager/SettingEditWindow.cs)는 값을 `valueToSet`에 stage하고 Apply 때 반영한다. string/list 편집, reorder, HSL/RGBA color, vector precision 같은 상세 기능이 있다.
- config tag의 `ReadOnly`와 `Browsable`은 창이 열린 동안 frame마다 다시 읽고 필터 상태가 변하면 rebuild한다. 모든 `Set()` 직전에도 재확인하며, 상세 창은 setting이 hidden이 되면 닫고 readonly면 control을 disable한다. 근거: `ConfigSettingEntry.cs` L64-L117, `SettingEntryBase.cs` L126-L140, `SettingsWindow.cs` L173-L187.

이 동적 권한 경로는 서버 접속 후 이미 열린 설정이 뒤늦게 잠기는 상황을 직접 방어한다. 두 구현 사이에서 shudnal의 가장 실질적인 운영상 우위다.

### 7.4 동기화와 server hidden

[`ConfigSynchronizationInfo.cs`](refer/shudnal-ConfigurationManager/ConfigSynchronizationInfo.cs)는 다음 provider를 구분한다.

- Conditional Config Sync: compile-time type로 찾고 conditional ownership/override/policy-control 상태를 표시·toggle한다.
- ServerSync: `ServerSync.OwnConfigEntryBase`와 `SynchronizedConfig`를 reflection으로 읽는다.
- Jotunn: assembly/type/property 이름을 reflection으로 읽는다.

메인 row에 server-controlled `S` 또는 client-controlled `C`를 표시하고 provider, mod default, server override와 관리자/세션 요구사항을 tooltip으로 제공한다. CCS에서 권한과 호환 세션 조건이 맞으면 관리자 policy toggle도 가능하다. ServerSync/Jotunn은 표시만 한다. 근거: sync class L62-L241, `SettingsWindow.cs` L569-L608.

hidden-settings 파일은 `PluginGUID=Section=Key` 목록을 읽어 CCS `CustomSyncedValue<List<string>>`로 전달하고, non-admin client의 UI에서 제거한다. 이는 파일 자체 수정이나 다른 UI를 막는 보안 enforcement가 아니라 presentation filter다. 근거: `ValheimConfigurationManager.cs` L25-L147, L183-L186.

### 7.5 파일 편집기와 localization

- [`ConfigFilesEditor.cs`](refer/shudnal-ConfigurationManager/ConfigFilesEditor.cs)는 config, plugin, 자체 trash root를 tree로 보여 준다.
- cfg/json/yaml/yml 검색, 새 파일/폴더, rename, save, trash 이동, JSON/YAML 문법 검증, word wrap/rich text/font 설정을 제공한다.
- FileSystemWatcher로 config/plugin subtree 변경 시 tree cache를 무효화한다.
- 별도 locale loader는 없다. 대신 거의 모든 label/button/tooltip/placeholder를 `Text - ...` ConfigEntry로 노출한다. 사용자가 cfg를 번역하는 **수동 config-driven localization**이며 OS/게임 언어에 따른 자동 locale 전환은 아니다. 근거: main class L339-L403.

### 7.6 장점

- Valheim input, pause, menu, GUI scale까지 한 흐름으로 통합한다.
- Split/List View와 즉시/상세 편집의 두 UX를 함께 제공한다.
- dynamic readonly/browsable을 표시 시점과 write 시점 모두 방어한다.
- CCS 정책 control, ServerSync/Jotunn 상태를 사용자에게 설명한다.
- file editor가 create/rename/trash/validation까지 제공해 Azumatt의 raw editor보다 기능적이다.
- current HEAD/manifest/Thunderstore 버전의 추적성이 좋고 분석일에도 변경이 이어지고 있다.

### 7.7 확인된 위험과 결함 후보

| 우선순위 | 항목 | 코드 근거/영향 |
|---|---|---|
| 높음 | 비재현 build | csproj L61-L133의 `F:\Steam\...`, `..\Assemblies\...` DLL 경로가 clone에 없다. restore는 됐지만 15 warnings/477 errors로 compile이 막혔다. |
| 높음 | packaging 연결 누락 | `ILRepack.targets`는 publish pipeline을 정의하지만 csproj는 `<None>`으로만 포함하고 import하지 않는다. update script가 요구하는 sibling `API/CommonPublish.ps1`도 없다. |
| 높음 | file editor 경로 이탈 | `ConfigFilesEditor.cs` L342-L372, L572-L603은 입력을 바로 `Path.Combine`하고 canonical containment를 확인하지 않는다. `..` 또는 rooted path로 의도 root 밖 생성/이동이 가능하다. |
| 높음 | 상세 Flags UI freeze | `SettingEditWindow.cs` L850-L899에서 한 label이 max width보다 길면 index가 전진하지 않는 loop 경로가 있다. 메인 drawer의 guard가 상세 창에는 없다. |
| 높음 | Valheim update 민감성 | publicized assembly와 다수 concrete Harmony target에 결합한다. method/field 변경이 input/menu/scale 또는 load를 깨뜨릴 수 있다. |
| 중간 | 복합값 비교 오류 | `ConfigurationManagerStyles.cs` L413-L423이 일반 값을 `ToString()`으로 비교한다. `List<string>` 내용 변경이 같다고 판단돼 Apply/변경 강조가 틀릴 수 있다. |
| 중간 | hidden 목록 live rebuild 없음 | watcher/CCS 값 갱신 후 열린 목록에 `BuildFilteredSettingList()` 신호가 없다. 다른 rebuild 계기까지 기존 항목이 남을 수 있다. |
| 중간 | setting order 문서 반대 | 모델 주석은 낮은 Order가 위라고 하지만 실제 L698은 내림차순이다. |
| 중간 | file tree/외부 변경 | junction cycle visited-set, optimistic concurrency, watcher dispose/thread synchronization이 없다. |
| 중간 | pause ownership | 이 모드가 pause를 걸었는지 별도 기록하지 않아 다른 이유의 pause를 닫을 때 해제할 수 있다. |
| 중간 | 잘못된 vector 입력 | 메인 vector parser 실패의 out 값 0이 즉시 적용될 수 있다. |
| 낮음 | localization 방식 | 자동 locale가 아니라 사용자가 자체 cfg 문구를 직접 번역해야 한다. |

## 8. 빌드·배포 검증

### AzumattDev

실행한 기준 명령:

```powershell
dotnet restore ConfigurationManager.sln
dotnet build ConfigurationManager.sln -c Release
```

- restore는 성공했다.
- solution build는 Mono analyzer direct path의 CS0006과 hard-coded post-build copy의 MSB3030으로 실패했다.
- IL2CPP project는 별도 검증에서 main DLL compile/output까지 진행됐으나 53 warnings 후 동일 MSB3030으로 전체 build가 실패했다.
- 실패는 외부 game DLL 부재가 아니라 repository build definition 자체의 portability 문제를 포함한다.

### shudnal

실행한 기준 명령:

```powershell
dotnet restore ConfigurationManager.sln
dotnet build ConfigurationManager.sln -c Release
```

- NuGet restore는 성공했다.
- build는 `0Harmony`, BepInEx, publicized Valheim assemblies, ConditionalConfigSync, Unity modules가 csproj의 hard-coded 위치에 없어 15 warnings/477 errors로 실패했다.
- 이는 source 문법 오류를 입증하지 않는다. 요구 assembly를 올바른 Valheim/BepInEx 개발 환경에서 공급하기 전에는 compile 성공 여부를 확정할 수 없다.

두 저장소 모두 test project가 없다. Azumatt에는 release/manual NuGet pack/push workflow 하나가 있지만 test 단계가 없고 오래된 action을 사용한다. shudnal에는 CI workflow가 없다.

## 9. 배포 버전과 유지보수

| 항목 | AzumattDev | shudnal |
|---|---|---|
| GitHub release | 없음 | `test` release 1개; semantic 제품 release 아님 |
| GitHub tag | 없음 | `test`, `BogWitchPTB` |
| 실제 주 배포 | [Thunderstore: Azus UnOfficial ConfigManager](https://thunderstore.io/c/valheim/p/Azumatt/Azus_UnOfficial_ConfigManager/) | [Thunderstore](https://thunderstore.io/c/valheim/p/shudnal/ConfigurationManager/) + [Nexus](https://www.nexusmods.com/valheim/mods/2746) |
| 조사 시 최신 배포 | Thunderstore 18.4.4 | Thunderstore 1.1.14, Nexus 1.1.13 |
| source 대응 | master 18.4.1, 공개 Improvements 18.4.3; 18.4.4 SHA 불명 | HEAD/manifest/Thunderstore 1.1.14 일치 |
| 최근 master 활동 | 2025-03-12 | 2026-07-15 |

Azumatt의 소스 범위는 범용/IL2CPP까지 넓지만 실제 Thunderstore package는 Valheim/BepInEx 5 배포다. repository capability와 배포 binary 범위를 구분해야 한다.

## 10. 라이선스 주의

두 root license 파일의 본문은 모두 **GNU LGPL v3**다.

- Azumatt: [`LICENSE`](refer/AzumattDev-BepInEx.ConfigurationManager/LICENSE)는 LGPL-3.0이고 assembly metadata도 LGPL-3.0이지만 주요 source header는 “GNU GPL v3.0”이라고 적는다.
- shudnal: [`LICENSE.txt`](refer/shudnal-ConfigurationManager/LICENSE.txt)는 LGPL-3.0인데 README, package README와 source header는 GPLv3라고 적는다. shudnal 이력에서는 2025-01-22에 license file이 GPL에서 LGPL로 바뀌었지만 문서가 함께 갱신되지 않았다.

이 문서는 법률 자문이 아니다. 다만 코드를 복사·수정·재배포하기 전에 현재 license file, source header와 README의 불일치를 upstream에 확인하고 저작권 고지와 source 제공 의무를 정리해야 한다.

## 11. 선택 가이드

### Azumatt를 reference로 삼기 좋은 경우

- Valheim에 한정되지 않는 BepInEx Configuration Manager를 만들 때
- Mono와 IL2CPP를 같은 core로 유지하려 할 때
- plugin pin, 대규모 목록 viewport culling, pool/cache 패턴이 필요할 때
- 단순한 2열 UI와 즉시 편집이 상세 dialog보다 중요할 때

먼저 가져오지 말아야 할 부분은 현재 `Directory.Build.props`의 post-build target, IL2CPP OtherFiles 수집, raw file 삭제, color/input의 결함 후보 코드다.

### shudnal을 reference로 삼기 좋은 경우

- Valheim modpack의 설정 UX를 개선할 때
- ServerSync/Jotunn/Conditional Config Sync 상태를 사용자에게 명확히 보여 줄 때
- 접속 후 바뀌는 readonly/browsable을 write 직전까지 강제해야 할 때
- resizable Split View, 상세 staged editor, file tree/validation이 필요할 때

먼저 격리하거나 고쳐야 할 부분은 game/publicized assembly 경로, file editor containment, 상세 flags loop, 복합값 equality, hidden-list live rebuild다.

### 두 구현을 합친다면

가장 합리적인 조합은 다음과 같다.

1. Azumatt의 game-agnostic shared core와 Mono/IL2CPP adapter 경계를 기반으로 둔다.
2. Azumatt의 plugin/category culling과 pinning을 유지한다.
3. shudnal의 window state/resize, Split View와 staged Setting Edit Window를 UI module로 분리한다.
4. shudnal의 dynamic attribute/write guard와 sync-provider adapter를 optional Valheim integration으로 둔다.
5. file editor는 공통 기능을 그대로 복사하지 말고 canonical path containment, atomic save, backup/trash metadata, external-change conflict를 먼저 설계한다.
6. game-specific Harmony patch는 core DLL에서 분리한다.
7. build path를 props/environment variable로 외부화하고 unit test 가능한 search/sort/metadata/equality/sync logic부터 test를 추가한다.

## 12. 근거 파일 인덱스

### AzumattDev

- 정체성/lifecycle/UI: [`ConfigurationManager.Shared/ConfigurationManager.cs`](refer/AzumattDev-BepInEx.ConfigurationManager/ConfigurationManager.Shared/ConfigurationManager.cs)
- Mono 탐색: [`ConfigurationManager/SettingSearcher.cs`](refer/AzumattDev-BepInEx.ConfigurationManager/ConfigurationManager/SettingSearcher.cs)
- IL2CPP 탐색: [`ConfigurationManager.IL2CPP/SettingSearcher.cs`](refer/AzumattDev-BepInEx.ConfigurationManager/ConfigurationManager.IL2CPP/SettingSearcher.cs)
- drawer: [`SettingFieldDrawer.cs`](refer/AzumattDev-BepInEx.ConfigurationManager/ConfigurationManager.Shared/SettingFieldDrawer.cs)
- metadata model/template: [`SettingEntryBase.cs`](refer/AzumattDev-BepInEx.ConfigurationManager/ConfigurationManager.Shared/SettingEntryBase.cs), [`ConfigurationManagerAttributes.cs`](refer/AzumattDev-BepInEx.ConfigurationManager/ConfigurationManagerAttributes.cs)
- build/package: [`Directory.Build.props`](refer/AzumattDev-BepInEx.ConfigurationManager/Directory.Build.props), [`ConfigurationManager.csproj`](refer/AzumattDev-BepInEx.ConfigurationManager/ConfigurationManager/ConfigurationManager.csproj), [`ConfigurationManager.IL2CPP.csproj`](refer/AzumattDev-BepInEx.ConfigurationManager/ConfigurationManager.IL2CPP/ConfigurationManager.IL2CPP.csproj)
- 설명/변경: [`README.md`](refer/AzumattDev-BepInEx.ConfigurationManager/README.md), [`CHANGELOG.md`](refer/AzumattDev-BepInEx.ConfigurationManager/CHANGELOG.md)

### shudnal

- 정체성/config/lifecycle: [`ConfigurationManager.cs`](refer/shudnal-ConfigurationManager/ConfigurationManager.cs)
- main UI/search/sort: [`SettingsWindow.cs`](refer/shudnal-ConfigurationManager/SettingsWindow.cs)
- 즉시/상세 drawer: [`SettingFieldDrawer.cs`](refer/shudnal-ConfigurationManager/SettingFieldDrawer.cs), [`SettingEditWindow.cs`](refer/shudnal-ConfigurationManager/SettingEditWindow.cs)
- dynamic metadata/write guard: [`ConfigSettingEntry.cs`](refer/shudnal-ConfigurationManager/ConfigSettingEntry.cs), [`SettingEntryBase.cs`](refer/shudnal-ConfigurationManager/SettingEntryBase.cs)
- sync adapter: [`ConfigSynchronizationInfo.cs`](refer/shudnal-ConfigurationManager/ConfigSynchronizationInfo.cs)
- Valheim integration: [`ValheimConfigurationManager.cs`](refer/shudnal-ConfigurationManager/ValheimConfigurationManager.cs)
- file editor: [`ConfigFilesEditor.cs`](refer/shudnal-ConfigurationManager/ConfigFilesEditor.cs)
- build/package: [`ConfigurationManager.csproj`](refer/shudnal-ConfigurationManager/ConfigurationManager.csproj), [`ILRepack.targets`](refer/shudnal-ConfigurationManager/ILRepack.targets), [`manifest.json`](refer/shudnal-ConfigurationManager/package/thunderstore/ConfigurationManager/manifest.json)
- 설명/변경: [`README.md`](refer/shudnal-ConfigurationManager/README.md), [`CHANGELOG.md`](refer/shudnal-ConfigurationManager/CHANGELOG.md)

## 13. 재현 명령

```powershell
git -C refer/AzumattDev-BepInEx.ConfigurationManager rev-parse HEAD
git -C refer/AzumattDev-BepInEx.ConfigurationManager status --short --branch

git -C refer/shudnal-ConfigurationManager rev-parse HEAD
git -C refer/shudnal-ConfigurationManager status --short --branch
```

분석 보조 메모는 [`refer/.analysis/`](refer/.analysis/)에 보존했다. 최종 판단은 이 문서가 기준이며, 보조 메모에는 각 구현의 더 세밀한 줄 단위 추적과 잠재 결함 설명이 들어 있다.
