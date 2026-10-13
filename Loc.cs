using System.IO;
using System.Text.Json;
using System.Windows;

namespace TrikiMusicController;

public enum AppLanguage
{
    Ru,
    En,
    Pl
}

public static class Loc
{
    public static AppLanguage CurrentLanguage { get; private set; } = AppLanguage.Ru;
    public static event Action? LanguageChanged;

    private static readonly Dictionary<string, string> StringsRu = new()
    {
        // Top Bar & App
        ["App_Title"] = "Triki Music Controller - Студия обучения и распознавания жестов",
        ["App_Subtitle"] = "Самообучаемый контроллер на ваших движениях",
        ["Status_Connected"] = "🟢 Подключено",
        ["Status_Connecting"] = "🟡 Подключение...",
        ["Status_Scanning"] = "🟡 Поиск устройства...",
        ["Status_Disconnected"] = "🔴 Отключено",
        ["Btn_Connect"] = "Подключить",
        ["Btn_Disconnect"] = "Отключить",
        ["Btn_Stop"] = "Остановить",
        ["Btn_MinimizeTray"] = "В трей",
        ["Model_Status_Ready"] = "🧠 Модель: Готова к обучению",
        ["Model_Status_Clean"] = "⚪ Система чистая: 0 жестов (ложные срабатывания выключены)",

        // Tab 1: Live
        ["Tab_Recognition"] = "🎮 Распознавание и Управление",
        ["Section_LiveRecognition"] = "ЖИВОЕ РАСПОЗНАВАНИЕ ДЕЙСТВИЙ",
        ["HandState_Clean"] = "⚪ Система чистая: запишите жесты в студии обучения",
        ["HandState_ButtonHeld"] = "🟢 Кнопка зажата: делайте жест!",
        ["HandState_ButtonIdle"] = "⚪ Зажмите кнопку Triki для распознавания жеста",

        // Action Cards
        ["Action_Prev"] = "НАЗАД",
        ["Action_Prev_Desc"] = "Взмах влево",
        ["Action_VolDown"] = "ТИШЕ",
        ["Action_VolDown_Desc"] = "Крутить влево",
        ["Action_PlayPause"] = "ПАУЗА / ПЛЕЙ",
        ["Action_PlayPause_Desc"] = "Взмах вниз ⚡",
        ["Action_VolUp"] = "ГРОМЧЕ",
        ["Action_VolUp_Desc"] = "Крутить вправо",
        ["Action_Next"] = "ВПЕРЁД",
        ["Action_Next_Desc"] = "Взмах вправо",
        ["Action_Idle"] = "Покой / Шум руки",
        ["Action_TrainedSuffix"] = "(Обучен)",
        ["Action_DisabledSuffix"] = "(Выключен)",

        // Live Sensors
        ["Sensors_Title"] = "ПОКАЗАТЕЛИ ДАТЧИКОВ В РЕАЛЬНОМ ВРЕМЕНИ",
        ["Sensors_Accel"] = "АКСЕЛЕРОМЕТР (УСКОРЕНИЕ & НАКЛОН)",
        ["Sensors_Gyro"] = "ГИРОСКОП (СКОРОСТЬ ВРАЩЕНИЯ)",
        ["Sensors_Button"] = "КНОПКА ТРИКИ",
        ["Button_State_Released"] = "ОТПУЩЕНА",
        ["Button_State_Held"] = "ЗАЖАТА (СЛУШАЮ ЖЕСТ)",
        ["Gyro_Unit"] = "°/с",

        // Modes & Settings
        ["Settings_Title"] = "РЕЖИМЫ И НАСТРОЙКИ",
        ["Settings_SendMediaKeys"] = "Отправлять мультимедиа-клавиши в Windows",
        ["Settings_SendMediaKeys_Desc"] = "Управление системным плеером (Spotify/Яндекс/Браузер)",
        ["Settings_AutoReconnect"] = "Автоматически переподключаться при обрыве связи",
        ["Settings_Tip_Title"] = "💡 Как обучить систему под ваши жесты:",
        ["Settings_Tip_Text"] = "Перейдите на вкладку «Студия обучения», выберите нужное действие и сделайте пару естественных записей своих движений!",

        // Event Log
        ["Log_Title"] = "ЖУРНАЛ СОБЫТИЙ",
        ["Log_OpenFile"] = "📄 Открыть лог-файл",
        ["Log_Clear"] = "Очистить",
        ["Log_Col_Time"] = "Время",
        ["Log_Col_Action"] = "Действие",
        ["Log_Col_Details"] = "Детали",

        // Tab 2: Training Studio
        ["Tab_Training"] = "🎓 Студия обучения (Запись ваших движений)",
        ["Step1_Title"] = "ШАГ 1: ВЫБЕРИТЕ ДЕЙСТВИЕ ДЛЯ ОБУЧЕНИЯ",
        ["Train_PlayPause"] = "⏯ Пауза / Плей (Взмах вниз)",
        ["Train_Next"] = "⏭ Вперёд (Взмах вправо)",
        ["Train_Prev"] = "⏮ Назад (Взмах влево)",
        ["Train_VolUp"] = "🔊 Громче (Кручение вправо)",
        ["Train_VolDown"] = "🔉 Тише (Кручение влево)",
        ["Train_Idle"] = "🧘 Покой / Шум руки (НЕ нажимать ничего)",

        ["Step2_DurationLabel"] = "Длительность сессии записи: ",
        ["Step2_Dur5"] = "5 сек",
        ["Step2_Dur7"] = "7 сек (рекомендуется)",
        ["Step2_Dur10"] = "10 сек",
        ["Step2_TipReps"] = "💡 За одну сессию сделайте жест 3-5 раз подряд!",
        ["Step2_BtnRecord"] = "🔴 НАЧАТЬ ЗАПИСЬ",
        ["Step2_BtnRecordSub"] = "(или клик по кнопке Triki 🔘)",
        ["Step2_StatusInitial"] = "Нажмите «Начать запись» или кликните кнопку Triki — начнётся отсчёт 3 сек",
        ["Step2_StatusReady"] = "Готово",
        ["Step2_LastRecordNone"] = "Последняя запись: нет данных",

        ["Step3_Title"] = "ШАГ 3: СОХРАНЕННЫЕ ОБРАЗЦЫ И ОБУЧЕНИЕ МОДЕЛИ",
        ["Step3_BtnTrain"] = "🧠 ОБУЧИТЬ И ПРИМЕНИТЬ МОДЕЛЬ",
        ["Step3_BtnClear"] = "🗑 Очистить все записи",
        ["Step3_Col_Action"] = "Действие",
        ["Step3_Col_Samples"] = "Количество образцов",
        ["Step3_Col_PeakG"] = "Пиковая сила (G)",
        ["Step3_Col_PeakGyro"] = "Пиковая скорость (°/с)",
        ["Step3_Col_Status"] = "Статус профиля",
        ["Step3_Tip"] = "💡 СОВЕТ: Запишите 2-3 раза взмах вниз, 2-3 раза кручение и 1 раз покой (просто пошевелите пальцами, как в покое). Затем нажмите «Обучить и применить модель»!",

        // Dynamic Status & Dialogs
        ["Status_Trained_Good"] = "🟢 Отлично обучен",
        ["Status_Trained_NeedMore"] = "🟡 Нужно больше повторов",
        ["Status_Trained_None"] = "⚪ Не записан",
        ["Tray_Open"] = "Открыть панель",
        ["Tray_Exit"] = "Выход",
        ["Tray_Balloon_Text"] = "Приложение свернуто в трей и работает в фоне",
        ["Msg_ConnectFirst"] = "Сначала подключите Triki, чтобы получать данные с датчиков!",
        ["Msg_CountdownReady"] = "Приготовьтесь: {0}... Держите Triki между пальцами",
        ["Msg_RecordingRest"] = "🔴 ИДЕТ ЗАПИСЬ: Просто естественно держите Triki в руке и шевелите пальцами",
        ["Msg_RecordingAction"] = "🔴 ИДЕТ ЗАПИСЬ: Сделайте жест «{0}» 3-5 РАЗ ПОДРЯД!",
        ["Msg_RecordingDone"] = "Готово! Сохранено {0} образцов. Модель автоматически обучена и активна! ⚡",
        ["Msg_ExtractedReps"] = "Успешно извлечено {0} повторений! Пиковая сила: {1:F2} G, пик гироскопа: {2:F0} {3}",
        ["Msg_FewSamples"] = "Слишком мало данных. Проверьте соединение с Triki.",
        ["Msg_TrainSuccess"] = "Модель успешно обучена на {0} образцах ваших движений!\nТеперь контроллер точно распознает именно ваш стиль жестов.",
        ["Msg_TrainTitle"] = "Обучение завершено",
        ["Msg_ClearConfirm"] = "Удалить все сохраненные образцы движений?",
        ["Msg_ClearTitle"] = "Очистка",
        ["Msg_Cleared"] = "Все записи очищены.",
        ["DoubleClick_Log"] = "Двойной клик Triki -> Пауза / Плей"
    };

    private static readonly Dictionary<string, string> StringsEn = new()
    {
        // Top Bar & App
        ["App_Title"] = "Triki Music Controller - Gesture Recognition & Training Studio",
        ["App_Subtitle"] = "Self-learning gesture controller tailored to your movements",
        ["Status_Connected"] = "🟢 Connected",
        ["Status_Connecting"] = "🟡 Connecting...",
        ["Status_Scanning"] = "🟡 Searching for device...",
        ["Status_Disconnected"] = "🔴 Disconnected",
        ["Btn_Connect"] = "Connect",
        ["Btn_Disconnect"] = "Disconnect",
        ["Btn_Stop"] = "Stop",
        ["Btn_MinimizeTray"] = "To Tray",
        ["Model_Status_Ready"] = "🧠 Model: Ready for training",
        ["Model_Status_Clean"] = "⚪ Clean system: 0 gestures (false triggers disabled)",

        // Tab 1: Live
        ["Tab_Recognition"] = "🎮 Recognition & Control",
        ["Section_LiveRecognition"] = "LIVE ACTION RECOGNITION",
        ["HandState_Clean"] = "⚪ Clean system: record gestures in Training Studio",
        ["HandState_ButtonHeld"] = "🟢 Button held: make your gesture!",
        ["HandState_ButtonIdle"] = "⚪ Hold Triki button to recognize gesture",

        // Action Cards
        ["Action_Prev"] = "PREVIOUS",
        ["Action_Prev_Desc"] = "Flick left",
        ["Action_VolDown"] = "VOL DOWN",
        ["Action_VolDown_Desc"] = "Rotate left",
        ["Action_PlayPause"] = "PLAY / PAUSE",
        ["Action_PlayPause_Desc"] = "Flick down ⚡",
        ["Action_VolUp"] = "VOL UP",
        ["Action_VolUp_Desc"] = "Rotate right",
        ["Action_Next"] = "NEXT",
        ["Action_Next_Desc"] = "Flick right",
        ["Action_Idle"] = "Rest / Hand Tremor",
        ["Action_TrainedSuffix"] = "(Trained)",
        ["Action_DisabledSuffix"] = "(Disabled)",

        // Live Sensors
        ["Sensors_Title"] = "REAL-TIME SENSOR METRICS",
        ["Sensors_Accel"] = "ACCELEROMETER (G-FORCE & TILT)",
        ["Sensors_Gyro"] = "GYROSCOPE (ROTATION SPEED)",
        ["Sensors_Button"] = "TRIKI BUTTON",
        ["Button_State_Released"] = "RELEASED",
        ["Button_State_Held"] = "HELD (LISTENING)",
        ["Gyro_Unit"] = "°/s",

        // Modes & Settings
        ["Settings_Title"] = "MODES & SETTINGS",
        ["Settings_SendMediaKeys"] = "Send multimedia keys to Windows",
        ["Settings_SendMediaKeys_Desc"] = "Controls media player (Spotify / YouTube / Browser)",
        ["Settings_AutoReconnect"] = "Automatically reconnect on connection drop",
        ["Settings_Tip_Title"] = "💡 How to train the system for your gestures:",
        ["Settings_Tip_Text"] = "Open the 'Training Studio' tab, select an action, and perform a few natural recordings of your gestures!",

        // Event Log
        ["Log_Title"] = "EVENT LOG",
        ["Log_OpenFile"] = "📄 Open log file",
        ["Log_Clear"] = "Clear",
        ["Log_Col_Time"] = "Time",
        ["Log_Col_Action"] = "Action",
        ["Log_Col_Details"] = "Details",

        // Tab 2: Training Studio
        ["Tab_Training"] = "🎓 Training Studio (Record your movements)",
        ["Step1_Title"] = "STEP 1: SELECT ACTION TO TRAIN",
        ["Train_PlayPause"] = "⏯ Play / Pause (Flick down)",
        ["Train_Next"] = "⏭ Next Track (Flick right)",
        ["Train_Prev"] = "⏮ Previous Track (Flick left)",
        ["Train_VolUp"] = "🔊 Volume Up (Rotate right)",
        ["Train_VolDown"] = "🔉 Volume Down (Rotate left)",
        ["Train_Idle"] = "🧘 Rest / Hand Tremor (Do NOT press anything)",

        ["Step2_DurationLabel"] = "Recording session duration: ",
        ["Step2_Dur5"] = "5 sec",
        ["Step2_Dur7"] = "7 sec (recommended)",
        ["Step2_Dur10"] = "10 sec",
        ["Step2_TipReps"] = "💡 Perform the gesture 3-5 times in a row during one session!",
        ["Step2_BtnRecord"] = "🔴 START RECORDING",
        ["Step2_BtnRecordSub"] = "(or click Triki button 🔘)",
        ["Step2_StatusInitial"] = "Press 'Start Recording' or click Triki button — 3 sec countdown will begin",
        ["Step2_StatusReady"] = "Ready",
        ["Step2_LastRecordNone"] = "Last recording: no data",

        ["Step3_Title"] = "STEP 3: SAVED SAMPLES & MODEL TRAINING",
        ["Step3_BtnTrain"] = "🧠 TRAIN & APPLY MODEL",
        ["Step3_BtnClear"] = "🗑 Clear all recordings",
        ["Step3_Col_Action"] = "Action",
        ["Step3_Col_Samples"] = "Sample count",
        ["Step3_Col_PeakG"] = "Peak force (G)",
        ["Step3_Col_PeakGyro"] = "Peak speed (°/s)",
        ["Step3_Col_Status"] = "Profile status",
        ["Step3_Tip"] = "💡 TIP: Record 2-3 down flicks, 2-3 rotations, and 1 rest session (gently move fingers in rest). Then press 'Train & apply model'!",

        // Dynamic Status & Dialogs
        ["Status_Trained_Good"] = "🟢 Well trained",
        ["Status_Trained_NeedMore"] = "🟡 Needs more reps",
        ["Status_Trained_None"] = "⚪ Not recorded",
        ["Tray_Open"] = "Open Panel",
        ["Tray_Exit"] = "Exit",
        ["Tray_Balloon_Text"] = "App minimized to system tray and active in background",
        ["Msg_ConnectFirst"] = "Connect Triki first to receive sensor data!",
        ["Msg_CountdownReady"] = "Get ready: {0}... Hold Triki between fingers",
        ["Msg_RecordingRest"] = "🔴 RECORDING: Naturally hold Triki in hand and gently move fingers",
        ["Msg_RecordingAction"] = "🔴 RECORDING: Perform '{0}' gesture 3-5 TIMES IN A ROW!",
        ["Msg_RecordingDone"] = "Done! Saved {0} samples. Model automatically trained and active! ⚡",
        ["Msg_ExtractedReps"] = "Extracted {0} repetitions! Peak force: {1:F2} G, peak gyro: {2:F0} {3}",
        ["Msg_FewSamples"] = "Too little data. Check your connection with Triki.",
        ["Msg_TrainSuccess"] = "Model successfully trained on {0} movement samples!\nNow the controller precisely recognizes your gesture style.",
        ["Msg_TrainTitle"] = "Training Complete",
        ["Msg_ClearConfirm"] = "Delete all saved movement samples?",
        ["Msg_ClearTitle"] = "Clear Recordings",
        ["Msg_Cleared"] = "All recordings cleared.",
        ["DoubleClick_Log"] = "Triki Double-Click -> Play / Pause"
    };

    private static readonly Dictionary<string, string> StringsPl = new()
    {
        // Top Bar & App
        ["App_Title"] = "Triki Music Controller - Studio uczenia i rozpoznawania gestów",
        ["App_Subtitle"] = "Samouczący się kontroler dostosowany do Twoich ruchów",
        ["Status_Connected"] = "🟢 Połączono",
        ["Status_Connecting"] = "🟡 Łączenie...",
        ["Status_Scanning"] = "🟡 Wyszukiwanie urządzenia...",
        ["Status_Disconnected"] = "🔴 Rozłączono",
        ["Btn_Connect"] = "Połącz",
        ["Btn_Disconnect"] = "Rozłącz",
        ["Btn_Stop"] = "Zatrzymaj",
        ["Btn_MinimizeTray"] = "Do zasobnika",
        ["Model_Status_Ready"] = "🧠 Model: Gotowy do uczenia",
        ["Model_Status_Clean"] = "⚪ Czysty system: 0 gestów (fałszywe wywołania wyłączone)",

        // Tab 1: Live
        ["Tab_Recognition"] = "🎮 Rozpoznawanie i Sterowanie",
        ["Section_LiveRecognition"] = "ROZPOZNAWANIE AKCJI NA ŻYWO",
        ["HandState_Clean"] = "⚪ Czysty system: nagraj gesty w studiu uczenia",
        ["HandState_ButtonHeld"] = "🟢 Przycisk przytrzymany: wykonaj gest!",
        ["HandState_ButtonIdle"] = "⚪ Przytrzymaj przycisk Triki, aby rozpoznać gest",

        // Action Cards
        ["Action_Prev"] = "POPRZEDNI",
        ["Action_Prev_Desc"] = "Machnięcie w lewo",
        ["Action_VolDown"] = "CISZEJ",
        ["Action_VolDown_Desc"] = "Obrót w lewo",
        ["Action_PlayPause"] = "PAUZA / ODTWÓRZ",
        ["Action_PlayPause_Desc"] = "Machnięcie w dół ⚡",
        ["Action_VolUp"] = "GŁOŚNIEJ",
        ["Action_VolUp_Desc"] = "Obrót w prawo",
        ["Action_Next"] = "NASTĘPNY",
        ["Action_Next_Desc"] = "Machnięcie w prawo",
        ["Action_Idle"] = "Spoczynek / Szum dłoni",
        ["Action_TrainedSuffix"] = "(Wyuczony)",
        ["Action_DisabledSuffix"] = "(Wyłączony)",

        // Live Sensors
        ["Sensors_Title"] = "CZUJNIKI W CZASIE RZECZYWISTYM",
        ["Sensors_Accel"] = "AKCELEROMETR (PRZYSPIESZENIE & POCHYLENIE)",
        ["Sensors_Gyro"] = "ŻYROSKOP (PRĘDKOŚĆ OBROTOWA)",
        ["Sensors_Button"] = "PRZYCISK TRIKI",
        ["Button_State_Released"] = "ZWOLNIONY",
        ["Button_State_Held"] = "PRZYTRZYMANY (NASŁUCHIWANIE)",
        ["Gyro_Unit"] = "°/s",

        // Modes & Settings
        ["Settings_Title"] = "TRYBY I USTAWIENIA",
        ["Settings_SendMediaKeys"] = "Wysyłaj klawisze multimedialne do Windows",
        ["Settings_SendMediaKeys_Desc"] = "Sterowanie odtwarzaczem systemowym (Spotify / YouTube / Przeglądarka)",
        ["Settings_AutoReconnect"] = "Automatycznie łącz ponownie po utracie połączenia",
        ["Settings_Tip_Title"] = "💡 Jak nauczyć system Twoich gestów:",
        ["Settings_Tip_Text"] = "Przejdź do zakładki «Studio uczenia», wybierz akcję i nagraj kilka naturalnych ruchów ręki!",

        // Event Log
        ["Log_Title"] = "DZIENNIK ZDARZEŃ",
        ["Log_OpenFile"] = "📄 Otwórz plik dziennika",
        ["Log_Clear"] = "Wyczyść",
        ["Log_Col_Time"] = "Czas",
        ["Log_Col_Action"] = "Akcja",
        ["Log_Col_Details"] = "Szczegóły",

        // Tab 2: Training Studio
        ["Tab_Training"] = "🎓 Studio uczenia (Nagrywanie Twoich ruchów)",
        ["Step1_Title"] = "KROK 1: WYBIERZ AKCJĘ DO NAUCZENIA",
        ["Train_PlayPause"] = "⏯ Pauza / Odtwórz (Machnięcie w dół)",
        ["Train_Next"] = "⏭ Następny (Machnięcie w prawo)",
        ["Train_Prev"] = "⏮ Poprzedni (Machnięcie w lewo)",
        ["Train_VolUp"] = "🔊 Głośniej (Obrót w prawo)",
        ["Train_VolDown"] = "🔉 Ciszej (Obrót w lewo)",
        ["Train_Idle"] = "🧘 Spoczynek / Szum dłoni (NIE naciskaj niczego)",

        ["Step2_DurationLabel"] = "Czas trwania sesji nagrywania: ",
        ["Step2_Dur5"] = "5 sek",
        ["Step2_Dur7"] = "7 sek (zalecane)",
        ["Step2_Dur10"] = "10 sek",
        ["Step2_TipReps"] = "💡 Podczas jednej sesji wykonaj gest 3-5 razy z rzędu!",
        ["Step2_BtnRecord"] = "🔴 ROZPOCZNIJ NAGRYWANIE",
        ["Step2_BtnRecordSub"] = "(lub kliknij przycisk Triki 🔘)",
        ["Step2_StatusInitial"] = "Kliknij «Rozpocznij nagrywanie» lub przycisk Triki — rozpocznie się odliczanie 3 sek",
        ["Step2_StatusReady"] = "Gotowe",
        ["Step2_LastRecordNone"] = "Ostatnie nagranie: brak danych",

        ["Step3_Title"] = "KROK 3: ZAPISANE PRÓBKI I UCZENIE MODELU",
        ["Step3_BtnTrain"] = "🧠 WYTRENUJ I ZASTOSUJ MODEL",
        ["Step3_BtnClear"] = "🗑 Wyczyść wszystkie nagrania",
        ["Step3_Col_Action"] = "Akcja",
        ["Step3_Col_Samples"] = "Liczba próbek",
        ["Step3_Col_PeakG"] = "Siła szczytowa (G)",
        ["Step3_Col_PeakGyro"] = "Prędkość szczytowa (°/s)",
        ["Step3_Col_Status"] = "Stan profilu",
        ["Step3_Tip"] = "💡 WSKAZÓWKA: Nagraj 2-3 machnięcia w dół, 2-3 obroty i 1 spoczynek (poruszaj palcami naturalnie jak w spoczynku). Następnie kliknij «Wytrenuj i zastosuj model»!",

        // Dynamic Status & Dialogs
        ["Status_Trained_Good"] = "🟢 Dobrze wyuczony",
        ["Status_Trained_NeedMore"] = "🟡 Wymaga więcej powtórzeń",
        ["Status_Trained_None"] = "⚪ Nie nagrano",
        ["Tray_Open"] = "Otwórz panel",
        ["Tray_Exit"] = "Wyjście",
        ["Tray_Balloon_Text"] = "Aplikacja zminimalizowana do zasobnika i działa w tle",
        ["Msg_ConnectFirst"] = "Najpierw połącz Triki, aby odbierać dane z czujników!",
        ["Msg_CountdownReady"] = "Przygotuj się: {0}... Trzymaj Triki między palcami",
        ["Msg_RecordingRest"] = "🔴 TRWA NAGRYWANIE: Po prostu naturalnie trzymaj Triki w dłoni i poruszaj palcami",
        ["Msg_RecordingAction"] = "🔴 TRWA NAGRYWANIE: Wykonaj gest «{0}» 3-5 RAZY Z RZĘDU!",
        ["Msg_RecordingDone"] = "Gotowe! Zapisano {0} próbek. Model automatycznie nauczony i aktywny! ⚡",
        ["Msg_ExtractedReps"] = "Pomyślnie wyodrębniono {0} powtórzeń! Siła szczytowa: {1:F2} G, szczyt żyroskopu: {2:F0} {3}",
        ["Msg_FewSamples"] = "Zbyt mało danych. Sprawdź połączenie z Triki.",
        ["Msg_TrainSuccess"] = "Model został pomyślnie wytrenowany na {0} próbkach Twoich ruchów!\nTeraz kontroler dokładnie rozpoznaje Twój styl gestów.",
        ["Msg_TrainTitle"] = "Uczenie zakończone",
        ["Msg_ClearConfirm"] = "Usunąć wszystkie zapisane próbki ruchów?",
        ["Msg_ClearTitle"] = "Czyszczenie",
        ["Msg_Cleared"] = "Wszystkie nagrania zostały wyczyszczone.",
        ["DoubleClick_Log"] = "Podwójne kliknięcie Triki -> Pauza / Odtwórz"
    };

    public static string Tr(string key)
    {
        var dict = CurrentLanguage switch
        {
            AppLanguage.En => StringsEn,
            AppLanguage.Pl => StringsPl,
            _ => StringsRu
        };

        if (dict.TryGetValue(key, out var val)) return val;
        if (StringsRu.TryGetValue(key, out var fallback)) return fallback;
        return key;
    }

    public static string Tr(string key, params object[] args)
    {
        var format = Tr(key);
        try
        {
            return string.Format(format, args);
        }
        catch
        {
            return format;
        }
    }

    public static void SetLanguage(AppLanguage language)
    {
        CurrentLanguage = language;
        ApplyResources();
        SaveConfig();
        LanguageChanged?.Invoke();
    }

    public static void ApplyResources()
    {
        var app = System.Windows.Application.Current;
        if (app == null) return;

        var dict = CurrentLanguage switch
        {
            AppLanguage.En => StringsEn,
            AppLanguage.Pl => StringsPl,
            _ => StringsRu
        };

        foreach (var (k, v) in dict)
        {
            app.Resources[k] = v;
        }

        if (app.MainWindow != null)
        {
            app.MainWindow.Title = Tr("App_Title");
        }
    }

    private static string GetConfigPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TrikiMusicController");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    public static void LoadConfig()
    {
        try
        {
            var path = GetConfigPath();
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Language", out var prop))
                {
                    var langStr = prop.GetString();
                    if (Enum.TryParse<AppLanguage>(langStr, true, out var lang))
                    {
                        CurrentLanguage = lang;
                    }
                }
            }
        }
        catch
        {
            // Ignore config read error, keep default
        }

        ApplyResources();
    }

    private static void SaveConfig()
    {
        try
        {
            var path = GetConfigPath();
            var data = new { Language = CurrentLanguage.ToString() };
            File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Ignore config write error
        }
    }
}
