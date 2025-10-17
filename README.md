# Semantic Build Versioning for Unity

이 패키지는 Unity 빌드 프로세스 중에 Git의 커밋 메시지(Conventional Commits)를 분석하여 프로젝트 버전을 자동으로 업데이트합니다.

## 기능

\- `fix:` 커밋 시 Patch 버전 증가 (1.0.0 -> 1.0.1)

\- `feat:` 커밋 시 Minor 버전 증가 (1.0.0 -> 1.1.0)

\- `feat!:`, `BREAKING CHANGE:` 포함 시 Major 버전 증가 (1.0.0 -> 2.0.0)

\- 빌드 성공 시에만 Git 태그를 생성하고 원격 저장소에 푸시합니다.

\- 빌드 실패 시 프로젝트 버전을 원래대로 롤백합니다.


## 설치

Unity Package Manager에서 'Add package from git URL...'을 선택하고 이 저장소의 Git URL을 입력하세요.

