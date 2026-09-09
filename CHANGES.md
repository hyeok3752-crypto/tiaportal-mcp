# tiaportal-mcp 변경 이력

> 이 파일은 Claude Code가 자동으로 관리합니다.
> 코드 수정 시 반드시 이 파일에 이력을 추가하세요.

---

## [2026-05-20] 파서 개선 — StructuredText v4 지원

### 수정 파일

#### `tools/tia_xml_parser.py`
- **추가**: `StructuredText v4` 형식 파싱 지원
  - TIA Portal이 FOR/IF/CASE 등 SCL을 토큰 기반 XML로 저장하는 형식
  - namespace `http://www.siemens.com/automation/Openness/SW/NetworkSource/StructuredText/v4`
  - `reconstruct_st()` 함수로 Token/Blank/NewLine/Access/CallInfo 요소를 재조합해 SCL 코드 복원
- **수정**: `_find_all()`, `_find()`, `_components()` 헬퍼로 네임스페이스 무관 검색
- **변경**: 빈 네트워크 표시 `(no source)` → `(empty network)`

### 계기
- `205_Motion` Network 120 — `210_V90TO(i)` FOR 루프 호출이 파싱 안 됨
- 실제 코드: `FOR i := 1 TO Motion.servo_Quntity BY 1 DO 210_V90TO(i); END_FOR;`

### 현재 파싱 상태
- `exports/all/` — 전체 블록 XML 217개
- `exports/all_parsed/` — 전체 블록 압축 텍스트 217개 (최신)
- 빈 네트워크 325개는 실제로 비어 있는 것 (TIA Portal 구분선/플레이스홀더)

---

## [2026-05-20] 블록 분석 워크플로우 확립

### 추가 파일

#### `tools/tia_xml_parser.py`
- TIA Portal XML export → 압축 텍스트 변환 스크립트 (Python 3.8+)
- 원본 XML 수만 자 → 수백 줄로 압축, Claude 분석 시 토큰 대폭 절감
- **사용법:**
  ```
  python tools/tia_xml_parser.py exports/<블록명>.xml              # 콘솔 출력
  python tools/tia_xml_parser.py exports/<블록명>.xml exports/<블록명>.txt  # 파일 저장
  ```
- **출력 포함 항목:** 블록 타입/번호/언어, 인터페이스 변수, 네트워크별 제목+변수목록+파트 요약, SCL은 소스 직접 출력

### 블록 분석 표준 절차 (다음 대화에서 이 순서로)
1. `ExportBlock` 툴로 XML 저장 → `exports/` 폴더
2. `python tools/tia_xml_parser.py exports/<블록>.xml exports/<블록>.txt`
3. 압축된 `.txt` 파일을 Read로 읽어 분석
4. (필요시) 원본 XML의 특정 네트워크만 추가 확인

### ExportBlock softwarePath / blockPath 규칙
- `softwarePath`: `"BMA #01"` (고정)
- `blockPath`: 소프트웨어 트리 경로 그대로, 예시:
  - `"200_Motion/210_V90TO"`
  - `"700_OP/OP1/1701_BM10I_STG"`
  - `"000_System/001_System"`

### 주의사항
- 블록이 inconsistent 상태면 ExportBlock 실패 → TIA Portal에서 컴파일 먼저
- FB부터 컴파일해야 전체 컴파일 가능 (의존성 순서)
- exports/ 폴더: `C:\Users\USER\tiaportal-mcp\exports\`

---

## [2026-05-20] 초기 연결 문제 해결

### 환경
- TIA Portal V21
- MCP 서버: `C:\Users\USER\tiaportal-mcp\src\TiaMcpServer\bin\Release\net48\TiaMcpServer.exe`

### 수정 파일

#### `src/TiaMcpServer/Program.cs`
- **변경**: V21에서도 `Engineering.Resolver`를 항상 등록하도록 수정
- **Before**: V21이면 `Engineering.Resolver` 미등록, `Openness.Initialize()`만 호출
- **After**: 항상 `AppDomain.CurrentDomain.AssemblyResolve += Engineering.Resolver` 등록 후, V20+ 이면 `Openness.Initialize()`도 호출
- **이유**: `ReflectionTypeLoadException` (Siemens DLL 못 찾음) 해결

#### `src/TiaMcpServer/ModelContextProtocol/McpServer.cs`
- **변경**: `Connect()` 메서드 에러 메시지 상세화
- **After**: 예외 타입(`ex.GetType().Name`)과 `InnerException` 메시지를 포함한 detail 문자열 생성
- **이유**: "Failed to connect to TIA-Portal" 만으로는 원인 진단 불가

### 빌드 설정
- Debug → **Release** 빌드로 전환 (TIA Openness는 Release 빌드에서만 정상 동작)

### Claude Code 설정

#### `C:\Users\USER\.claude\settings.json`
- `permissions.allow`에 `mcp__tiaportal-mcp__*` 30개 툴 전체 추가
- **이유**: 매번 권한 프롬프트 없이 자동 실행하기 위함

### 결과
- `Connect` 툴 호출 성공: `CTe_BMA_PLC1_V21` 프로젝트에 연결됨
- `GetProjectTree` 로 프로젝트 구조 파악 완료 (PLC 1, HMI 4, 서보 59대, 인버터 10대 등)

---

## [2026-06-05] V21 → V20 전환

### 배경
- V20을 메인으로 사용하기로 변경
- V21 상태는 `v21-backup` 브랜치에 보존

### 수정 파일

#### `src/TiaMcpServer/TiaMcpServer.csproj`
- `Siemens.Collaboration.Net.TiaPortal.Packages.Openness` 버전 변경: `21.0.1744190253` → `20.0.1744190253`
- DLL HintPath 변경:
  - Before: `Portal V21\PublicAPI\V21\net48\Siemens.Engineering.Base.dll` + `Step7.dll`
  - After: `Portal V20\PublicAPI\V20\Siemens.Engineering.dll` + `Siemens.Engineering.Hmi.dll`
  - V20은 net48 하위폴더 없이 바로 V20/ 폴더에 DLL 위치

#### 환경변수 `TiaPortalLocation`
- 변경: `C:\Program Files\Siemens\Automation\Portal V21` → `C:\Program Files\Siemens\Automation\Portal V20`
- User 레벨로 설정 (Machine 레벨은 관리자 권한 필요)

### 비변경 사항
- `Program.cs`: 이미 기본값이 `TiaMajorVersion = 20`이어서 수정 불필요
- `Engineering.cs`: V20 레지스트리 경로(`TIAP20`) 이미 지원
- `Portal.cs`: 표준 `Siemens.Engineering.*` 네임스페이스 사용으로 호환

### 빌드 결과
- Release 빌드 성공 (경고 0, 오류 0)
- Connect 성공 확인

### V21로 되돌리는 방법
1. `git checkout v21-backup` (코드 복원)
2. `C:\Users\USER\.claude.json` 두 곳 수정:
   - `"--tia-major-version", "20"` → `"21"`
   - `env.TiaPortalLocation`: `Portal V20` → `Portal V21`
3. TiaMcpServer 프로세스 종료 후 `dotnet build -c Release`
4. VS Code 재시작 → Connect
- **주의**: `.claude.json` 수정을 빠뜨리면 DLL 버전과 인자 버전 불일치로 `EngineeringTargetInvocationException` 발생

---

<!-- 새 변경사항은 위에 추가 (최신순) -->
