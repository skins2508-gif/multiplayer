# Multiplayer

Unity로 제작한 캐릭터 기반 멀티플레이 슈팅 게임 프로젝트입니다. 정교한 밸런스보다 캐릭터마다 다른 공격 방식과 이동 능력을 조작하는 재미에 집중했습니다.

## 게임 다운로드 및 실행

**[Windows 실행용 ZIP 다운로드 — 약 514MB](https://github.com/skins2508-gif/multiplayer/releases/download/v2026.10.27/Multiplayer-Windows-x64-2026-10-27.zip)**

[릴리스 페이지](https://github.com/skins2508-gif/multiplayer/releases/tag/v2026.10.27)에서도 받을 수 있습니다.

1. ZIP 전체를 압축 해제합니다.
2. `Multiplayer/multiplayer.exe`를 실행합니다.
3. `multiplayer_Data`, `UnityPlayer.dll` 등 동봉 파일을 같은 폴더에 유지합니다.

Unity 설치 없이 실행할 수 있는 Windows 64비트 빌드입니다. 온라인 멀티플레이에는 인터넷 연결이 필요합니다. 캐서디 구르기 무적(0.72초)과 스태미너 50 소모를 포함했습니다.

## 개발 환경

- Unity 6000.3.20f1
- C# · Universal Render Pipeline(URP)
- Netcode for GameObjects · Unity Transport · Multiplayer Services

## 주요 구현

- 멀티플레이 세션 생성과 참가, 캐릭터 선택
- 네트워크 기반 공격 및 피해 처리
- 솔저: 라이플 사격, 미사일, 각성 중 연사와 이동 강화
- 겐지: 수리검 3연발, 2단 점프, 각성 카타나, 대시 공격, 탄환 반사
- 디바: 메카 일반 공격, 방어 매트릭스, 부스터, 탈출과 자폭, 파일럿 권총
- 캐서디: 리볼버 단발과 남은 탄약 연사, 구르기, 황야의 무법자
- 캐서디 구르기: 0.72초 무적, 스태미너 50 소모, 스태미너 부족 시 사용 제한

## 저장소 구성

- `Assets/Shooter/Scripts`: 캐릭터, 무기, 전투와 게임 진행 코드
- `Assets/Core/Scripts`: 이동, 점프, 능력치 등 공통 시스템
- `Assets/Blocks`: 멀티플레이 세션 관련 코드
- `Packages`: 패키지 의존성 정보
- `ProjectSettings/ProjectVersion.txt`: Unity 버전 정보
- `Assets`: 게임 씬, 프리팹, 캐릭터 모델, 텍스처·이미지, 머티리얼, 애니메이션, 음원 및 효과 등 게임에 필요한 에셋과 메타파일
- `ArtSource`: 게임 캐릭터·무기·맵·UI에 사용한 Blender, PSD, 이미지 등 제작 원본
- `ProjectSettings`: 입력, 렌더링, 플레이어 및 빌드 설정

이 저장소는 멀티플레이어 게임 코드와 Windows 실행용 빌드를 관리합니다. 게임 빌드는 Releases에서 배포합니다.

Code 탭에는 게임의 에셋·제작 원본·패키지 목록·프로젝트 설정을 포함합니다. 저장소를 내려받아 Unity Hub에서 Unity 6000.3.20f1로 열고, 패키지 복원과 임포트를 기다린 뒤 `Assets/Shooter/TestScenes/[BB] Shooter MultiplayerSession.unity` 씬을 열어 사용하세요.

시네마틱 관련 에셋·영상·스크립트, 설치 도구와 가상환경, 촬영 중간 프레임 및 백업은 제외했습니다. `Library`, `Temp`, `Logs`, `UserSettings` 등 Unity 생성 파일도 제외하며, 프로젝트를 열면 필요한 캐시는 다시 생성됩니다. 시네마틱 씬은 업로드용 빌드 설정에서 제외했습니다.

게임만 플레이하려면 위 Windows 실행용 ZIP을 받으세요. 실행용 ZIP은 2026-10-27 빌드이며, Code 탭의 후속 소스 변경은 별도로 다시 빌드해야 반영됩니다.

## 제작 경험

캐릭터별 공격과 이동 능력을 연결하고, 직접 플레이하며 피해량·자원 소모·재사용 대기시간을 조정했습니다. 에디터와 실제 빌드 환경의 차이에서 발생한 문제를 해결하며 빌드 테스트의 중요성을 배웠습니다. 구현과 문제 해결 과정에서는 AI를 활용하고, 결과를 실제 플레이로 검증했습니다.

타사 에셋과 코드, 의존성의 권리는 각 원저작자에게 있습니다. 이 저장소에 별도의 재배포 라이선스를 부여하지 않습니다.
