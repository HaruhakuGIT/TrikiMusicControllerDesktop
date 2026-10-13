# Triki Music Controller (PC Desktop) 🎵

[🇬🇧 English](#-english) | [🇵🇱 Polski](#-polski)

---

# 🇬🇧 English

> ⚠️ **LEGAL DISCLAIMER & NOTICE**:  
> This project is an **independent, non-commercial hobby experiment and DIY educational research project** created solely for personal learning, reverse engineering practice, and exploration of Bluetooth Low Energy (BLE) protocols and 6-axis Inertial Measurement Unit (IMU) gesture processing.  
> **This project is NOT affiliated, associated, authorized, endorsed by, or in any way officially connected with Żabka Polska sp. z o.o.**, its subsidiaries, or its affiliates. The names "Triki", "Żabka", "Żappka", and related marks or logos are registered trademarks of their respective owners. Any reference to these names is strictly for hardware compatibility identification and educational purposes under **Nominative Fair Use**. This repository does not contain, reverse engineer, or distribute any proprietary code, cloud endpoints, or confidential keys.

---

![C#](https://img.shields.io/badge/Language-C%23-blue.svg)
![.NET 9](https://img.shields.io/badge/Platform-.NET%209%20WPF-purple.svg)
![Windows](https://img.shields.io/badge/OS-Windows%2010%2F11-0078D6.svg)
![BLE](https://img.shields.io/badge/Bluetooth-LE%205.0%20(NUS)-0082FC.svg)
![License](https://img.shields.io/badge/License-MIT-green.svg)

**Triki Music Controller** is a Windows desktop application (WPF, .NET 9) that turns the **Triki** smart wearable BLE ring/puck into a wireless gesture-based media controller for your PC, complete with an interactive Gesture Training Studio.

---

## 🔍 How the Triki Hardware & Communication Works

The **Triki** device is a compact wearable gadget featuring:
1. **Bluetooth Low Energy (BLE 5.0) SoC** (Nordic Semiconductor architecture).
2. **6-Axis Inertial Measurement Unit (IMU)**: 3-axis accelerometer + 3-axis gyroscope.
3. **Tactile Push Button**: Physical click / hold detection.
4. **Rechargeable Li-Po Battery**: Onboard charging and voltage monitoring.

### 📡 BLE GATT Architecture
The application connects to the device using standard Bluetooth Low Energy GATT services:
* **Nordic UART Service (NUS)**:
  * Service UUID: `6e400001-b5a3-f393-e0a9-e50e24dcca9e`
  * RX Characteristic (Write command): `6e400002-b5a3-f393-e0a9-e50e24dcca9e`
  * TX Characteristic (Telemetry Notify): `6e400003-b5a3-f393-e0a9-e50e24dcca9e`
* **Battery Service (`0x180F`)**:
  * Battery Level (`0x2A19`): Reads charge percentage (0–100%).
* **Device Information Service (`0x180A`)**:
  * Firmware Revision (`0x2A26`) and Model Number (`0x2A24`).

### 📦 Packet Structure & Protocol
1. **Stream Activation**:  
   To start continuous IMU telemetry transmission, the app writes an 8-byte initialization command to the RX characteristic:  
   `0x20 0x10 0x00 0xD0 0x07 0x68 0x00 0x03`
2. **Incoming 14-Byte Telemetry Frame**:
   * **Byte 0**: Packet Header (`0x22`)
   * **Byte 1**: Button State (`0x00` = released, `0x01` = pressed/held)
   * **Bytes 2–7**: Accelerometer X, Y, Z (16-bit signed integers, scale: `2048 LSB/G`, ±16G range)
   * **Bytes 8–13**: Gyroscope X, Y, Z (16-bit signed integers, scale: `131 LSB/(°/s)`, ±250 dps range)

---

## 🧠 Gesture Recognition Engine

* **Button-Gated Recognition**:  
  While IMU telemetry streams continuously, gesture recognition is only active while the user holds down the Triki button. This ensures **0% false triggers** during walking, typing, or normal arm motion.
* **Interactive Gesture Training Studio**:  
  Users can train natural, custom gestures for their movements:
  * ⏯ **Play / Pause**: Quick downward flick (Flick Down).
  * ⏭ **Next Track**: Quick rightward flick (Flick Right).
  * ⏮ **Previous Track**: Quick leftward flick (Flick Left).
  * 🔊 **Volume Up**: Clockwise rotation (Roll Right).
  * 🔉 **Volume Down**: Counter-clockwise rotation (Roll Left).
  * 🧘 **Idle / Hand Noise**: Finger resting motion calibration.
* **Windows Media Key Emulation**:  
  Recognized gestures dispatch virtual keyboard events (`keybd_event`):
  * `VK_MEDIA_PLAY_PAUSE` (`0xB3`)
  * `VK_MEDIA_NEXT_TRACK` (`0xB0`)
  * `VK_MEDIA_PREV_TRACK` (`0xB1`)
  * `VK_VOLUME_UP` (`0xAF`) / `VK_VOLUME_DOWN` (`0xAE`)  
  Works out of the box with Spotify, YouTube Music, web browsers, and media players.

---

## 💻 Desktop Application Features

* **Real-Time Telemetry**: Live graphical progress bars for G-force and angular velocity (°/s).
* **Neon Action Cards**: Instant visual glow feedback upon gesture trigger.
* **Multilingual UI**: Instant runtime language switching between **English (EN)**, **Polski (PL)**, and **Russian (RU)**. Configuration saved to `%APPDATA%\TrikiMusicController\settings.json`.
* **System Tray Integration**:
  * Color-coded tray icons (Green = connected, Yellow = scanning, Red = disconnected).
  * Balloon notifications and context menu.

---

## 🛠 Build & Run Instructions

### Prerequisites
* Windows 10 (19041+) or Windows 11 with Bluetooth LE support.
* [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or higher.

### Build via Command Line
```bash
git clone git@github.com:HaruhakuGIT/TrikiMusicControllerPC.git
cd TrikiMusicControllerPC
dotnet build
dotnet run
```

### Publish Single-File Executable
```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

---
---

# 🇵🇱 Polski

> ⚠️ **ZASTRZEŻENIE PRAWNE (DISCLAIMER)**:  
> Niniejszy projekt jest **niezależnym, niekomercyjnym eksperymentem hobbystycznym oraz projektem edukacyjno-badawczym (DIY / Reverse Engineering)**, stworzonym wyłącznie do celów naukowych w zakresie protokołów Bluetooth Low Energy (BLE) oraz algorytmów rozpoznawania gestów z 6-osiowych czujników inercyjnych (IMU).  
> **Projekt NIE jest w żaden sposób powiązany, autoryzowany, sponsorowany ani powiązany z firmą Żabka Polska sp. z o.o.** ani jej podmiotami zależnymi. Nazwy „Triki”, „Żabka”, „Żappka” oraz powiązane znaki towarowe należą do ich prawnych właścicieli i zostały użyte wyłącznie w celach identyfikacji kompatybilności sprzętowej w ramach **dozwolonego użytku (Nominative Fair Use)**. Repozytorium nie zawiera ani nie dystrybuuje żadnego prawnie zastrzeżonego kodu, usług chmurowych ani kluczy dostępowych.

---

**Triki Music Controller** to aplikacja na system Windows (WPF, .NET 9), która przekształca interaktywne urządzenie **Triki** (BLE) w bezprzewodowy kontroler multimediów z wbudowanym studiem uczenia gestów.

---

## 🔍 Budowa i działanie urządzenia Triki

Fizyczne urządzenie **Triki** to miniaturowy gadżet ubieralny, wyposażony w:
1. **Mikrokontroler Bluetooth Low Energy (BLE 5.0)** (architektura Nordic Semiconductor).
2. **6-osiowy czujnik IMU**: 3-osiowy akcelerometr + 3-osiowy żyroskop.
3. **Przycisk fizyczny**: Rejestracja kliknięć i przytrzymania.
4. **Akumulator Li-Po** z wbudowanym kontrolerem ładowania.

### 📡 Protokół komunikacji BLE GATT
Aplikacja łączy się z urządzeniem za pomocą standardowych usług BLE:
* **Nordic UART Service (NUS)**:
  * UUID Usługi: `6e400001-b5a3-f393-e0a9-e50e24dcca9e`
  * Charakterystyka RX (Zapis komend): `6e400002-b5a3-f393-e0a9-e50e24dcca9e`
  * Charakterystyka TX (Powiadomienia telemetrii): `6e400003-b5a3-f393-e0a9-e50e24dcca9e`
* **Battery Service (`0x180F`)**:
  * Poziom baterii (`0x2A19`): Odczyt stanu naładowania w procentach (0–100%).
* **Device Information Service (`0x180A`)**:
  * Wersja oprogramowania układowego (`0x2A26`) oraz model (`0x2A24`).

### 📦 Format ramek danych
1. **Uruchomienie transmisji**:  
   Aplikacja wysyła 8-bajtową komendę aktywacyjną do charakterystyki RX:  
   `0x20 0x10 0x00 0xD0 0x07 0x68 0x00 0x03`
2. **Odbierana 14-bajtowa ramka telemetryczna**:
   * **Bajt 0**: Nagłówek ramki (`0x22`)
   * **Bajt 1**: Stan przycisku (`0x00` = zwolniony, `0x01` = wciśnięty/przytrzymany)
   * **Bajty 2–7**: Akcelerometr X, Y, Z (16-bitowe liczby ze znakiem int16, skala: `2048 LSB/G`, zakres ±16G)
   * **Bajty 8–13**: Żyroskop X, Y, Z (16-bitowe liczby ze znakiem int16, skala: `131 LSB/(°/s)`, zakres ±250 dps)

---

## 🧠 System rozpoznawania gestów

* **Filtrowanie przyciskiem (Button-Gating)**:  
  Telemetria jest przesyłana na bieżąco, ale analiza gestów następuje wyłącznie podczas przytrzymania przycisku Triki. Gwarantuje to **0% przypadkowych wywołań** podczas chodzenia, gestykulacji czy pisania na klawiaturze.
* **Interaktywne Studio Treningowe**:  
  Możliwość nagrania własnych, naturalnych ruchów:
  * ⏯ **Pauza / Odtwarzaj**: Dynamiczne machnięcie w dół (Flick Down).
  * ⏭ **Następny utwór**: Machnięcie w prawo (Flick Right).
  * ⏮ **Poprzedni utwór**: Machnięcie w lewo (Flick Left).
  * 🔊 **Głośniej**: Obrót w prawo (Roll Right).
  * 🔉 **Ciszej**: Obrót w lewo (Roll Left).
  * 🧘 **Spoczynek / Szum dłoni**: Kalibracja martwej strefy.
* **Emulacja klawiszy multimedialnych Windows**:  
  Wykryty gest wysyła zdarzenie klawiatury systemowej (`keybd_event`): `VK_MEDIA_PLAY_PAUSE`, `VK_MEDIA_NEXT_TRACK`, `VK_MEDIA_PREV_TRACK`, `VK_VOLUME_UP`/`DOWN`. Działa ze Spotify, YouTube Music, przeglądarkami oraz odtwarzaczami systemowymi.

---

## 💻 Funkcje aplikacji

* **Telemetria w czasie rzeczywistym**: Paski postępu siły przeciążenia (G) oraz prędkości kątowej (°/s).
* **Wielojęzyczność (Multilingual)**: Przełączanie w locie na pasku górnym pomiędzy językiem **polskim (PL)**, **angielskim (EN)** i **rosyjskim (RU)**. Zapis konfiguracji w `%APPDATA%\TrikiMusicController\settings.json`.
* **Działanie w zasobniku systemowym (Tray)**: Kolorowe ikony statusu połączenia, powiadomienia dymkowe oraz menu kontekstowe.

---

## 🛠 Kompilacja i uruchomienie

### Wymagania:
* Windows 10 (19041+) lub Windows 11 z obsługą Bluetooth LE.
* [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) lub nowszy.

### Kompilacja z poziomu konsoli:
```bash
git clone git@github.com:HaruhakuGIT/TrikiMusicControllerPC.git
cd TrikiMusicControllerPC
dotnet build
dotnet run
```

---

## 📄 Licencja

MIT License — dozwolone do bezpłatnego użytku prywatnego i edukacyjnego.
