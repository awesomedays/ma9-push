# 마구알림앱 (Ma9_Season_Push)

**마구마구 리마스터 경기 종료 자동 알림 유틸리티**
Windows 트레이 상주형 · 단일 실행 파일(EXE)

---

## 1. 개요

### 목적

본 애플리케이션은 **마구마구 리마스터 클라이언트 화면을 실시간 감시**하여 다음 이벤트를 자동 감지하고,
이를 **텔레그램 메시지로 즉시 알림**하는 **운영 보조용 Windows 유틸리티**입니다.

감지 대상 이벤트:

* 경기 종료 화면(END)
* 리그 결과 → 전체구장소식 화면

---

## 2. 주요 특징

* **단일 실행 파일(EXE)** 배포
* **콘솔 없는 WinExe**
* **Windows 시스템 트레이 상주**
* 사용자 개입 없는 **자동 감시**
* **OpenCV 기반 화면 인식**
* **디바운스 + 상태머신 구조**로 오탐 최소화
* 파일 로그 기반 운영 친화 설계

---

## 3. 실행 환경

| 항목     | 사양                      |
| ------ | ----------------------- |
| OS     | Windows 10 / 11 (64bit) |
| 런타임    | .NET 8 (Self-Contained) |
| 배포 형태  | 단일 EXE                  |
| 외부 의존성 | 없음 (네이티브 라이브러리 포함)      |

---

## 4. 빌드 사양

### .csproj 핵심 설정

```xml
TargetFramework: net8.0-windows
OutputType: WinExe
RuntimeIdentifier: win-x64
SelfContained: true
PublishSingleFile: true
PublishTrimmed: false
DebugType / DebugSymbols: 비활성
```

### 네이티브 라이브러리

* OpenCvSharp

  * OpenCvSharp4
  * OpenCvSharp4.runtime.win

### 리소스 정책

* PNG / ICO 리소스는 모두 **EmbeddedResource**
* 실행 시 외부 파일 의존성 없음

---

## 5. 런타임 아키텍처

### 전체 처리 흐름

```
Program.cs
 └─ CaptureService
     └─ EndSignDetector / LeagueNewsSignDetector
         └─ Debouncer
             └─ StateMachine
                 └─ TelegramNotifier
```

### 구성 요소 역할

* **CaptureService**
  지정 모니터 화면을 OpenCV `Mat`으로 캡처
* **Detector**
  템플릿 매칭 기반 이벤트 감지
* **Debouncer**
  연속 히트 기반 확정 판정
* **StateMachine**
  상태 전이 단일 책임 관리
* **TelegramNotifier**
  텔레그램 메시지 전송 및 실패 로깅

---

## 6. 상태 머신(AppState)

| 상태             | 설명          |
| -------------- | ----------- |
| Idle           | 초기 대기       |
| WatchingEnd    | 경기 종료 화면 감시 |
| WaitLeagueNews | 전체구장소식 대기   |

* 상태 전이는 **StateMachine.cs 단일 지점에서만 수행**
* Program.cs는 상태에 따라 감지 로직만 분기

---

## 7. 로그 및 관측 정책

* **파일 로그 단일 채널**
* 루프 단위 디버그 로그 제거됨
* 확정 이벤트 및 상태 전이 중심 로그 구성

로그 목적:

* 정상 동작 확인
* 오탐/미탐 분석
* 장기 운영 안정성 확보

---

## 8. 스크립트 구조

### Core / Entry

| 파일              | 역할              |
| --------------- | --------------- |
| Program.cs      | 메인 루프 및 오케스트레이션 |
| AppState.cs     | 상태 enum 정의      |
| StateMachine.cs | 상태 전이 및 상태 로그   |

### Capture / Detection

| 파일                        | 역할        |
| ------------------------- | --------- |
| CaptureService.cs         | 화면 캡처     |
| EndSignDetector.cs        | 경기 종료 감지  |
| LeagueNewsSignDetector.cs | 전체구장소식 감지 |
| Debouncer.cs              | 확정 판정     |

### Resource / Config

| 파일                | 역할              |
| ----------------- | --------------- |
| ResourceLoader.cs | 임베디드 리소스 로드     |
| AppPaths.cs       | 실행/로그 경로 계산     |
| AppConfig.cs      | 관측 주기 및 타임아웃 상수 |

### Logging / Notification

| 파일                  | 역할         |
| ------------------- | ---------- |
| Logger.cs           | 파일 로그 전담   |
| TelegramNotifier.cs | 텔레그램 알림 전송 |

### Tray / UI

| 파일                | 역할             |
| ----------------- | -------------- |
| TrayAppContext.cs | 트레이 상주 및 종료 처리 |
| TrayMenuStyle.cs  | 트레이 메뉴 UI 스타일  |

---

## 9. 최근 정리/경량화 내역

### 제거된 항목

* 루프 반복 디버그 로그
* 테스트/레거시 API
* 미사용 설정값 및 변수
* Console 출력 관련 코드

### 유지 정책

* 기능/동작 변경 없음
* 트레이 UI 유지
* 로그 구조 유지

---

## 10. 현재 상태 판정

* 기능 안정화 완료
* 코드 구조 슬림화 완료
* 운영 단계 진입 상태

다음 작업은 **기능 추가가 아닌 운영 완성도 개선(문서화·표준화·가이드)**을 목표로 합니다.

---

## 11. 향후 확장 후보

* 로그 포맷 표준화
* AppConfig 실사용 항목 문서화
* 운영자 체크리스트 작성
* 오탐/예외 분석 가이드 정리
