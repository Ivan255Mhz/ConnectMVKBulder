# 15. Уставки и тревоги

Глава о **прикладных** уставках МВК01. Это не регистры устройства и не
`AllowConfigurationWrite`: пороги хранятся в приложении, оцениваются по
снимкам `Mvk01Sample` и не требуют записи в МВК01.

Такой слой нужен, когда оператору важны не «сырые» значения, а события:
«канал 1 вышел за предупреждение», «канал 2 в аварии», «связь потеряна».
Уставки можно менять без перепрошивки и без записи в защищённую область.

## 15.1. Быстрый старт

Самый короткий сценарий — два JSON-файла и один сервис:

```csharp
using Mvk.MVK01.Setpoints;

await using Mvk01AlarmService alarms = await Mvk01AlarmService.StartAsync(
    "mvkmodbus.json",        // устройства
    "mvk-setpoints.json");   // наши уставки

using CancellationTokenSource cts = new();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

await alarms.RunAsync((alarm, _) =>
{
    Console.WriteLine(
        $"{alarm.Timestamp.ToLocalTime():HH:mm:ss}  {alarm.DeviceId}  "
        + $"{alarm.Setpoint.Name}: {alarm.PreviousState} -> {alarm.State} "
        + $"({alarm.Value:F3})");

    return ValueTask.CompletedTask;
}, cts.Token);
```

`StartAsync`:

- создаёт пул по `mvkmodbus.json`;
- загружает и проверяет уставки;
- готовит движок оценки.

`WatchAsync`/`RunAsync` поднимают watcher’ы по устройствам, оценивают пороги
с гистерезисом и отдают только переходы состояний. `RunAsync` — удобная обёртка
над `WatchAsync`: ошибка обработчика логируется, но наблюдение продолжается.

## 15.2. JSON-файл уставок

```json
{
  "setpoints": [
    {
      "id": "mvk-1/ch1/rms10-1000/warning",
      "deviceId": "mvk-1",
      "measurement": "Rms10To1000Acceleration",
      "channel": 1,
      "comparison": "Above",
      "threshold": 4.5,
      "hysteresis": 0.2,
      "activationDelay": "00:00:01",
      "severity": "Warning",
      "name": "Канал 1: предупреждение"
    },
    {
      "id": "mvk-1/ch1/rms10-1000/alarm",
      "deviceId": "mvk-1",
      "measurement": "Rms10To1000Acceleration",
      "channel": 1,
      "comparison": "Above",
      "threshold": 7.1,
      "hysteresis": 0.3,
      "severity": "Alarm",
      "name": "Канал 1: авария"
    }
  ]
}
```

Поля:

| Поле | Смысл |
|---|---|
| `id` | Уникальный идентификатор уставки |
| `deviceId` | `Id` устройства из `MvkModbus` |
| `measurement` | Номер параметра `Mvk01MeasurementNumber` |
| `channel` | Канал 1–8 |
| `comparison` | `Above`, `Below`, `Outside`, `Inside` |
| `threshold` | Порог (нижняя граница для диапазона) |
| `upperThreshold` | Верхняя граница для `Outside`/`Inside` |
| `hysteresis` | Гистерезис в единицах измерения |
| `activationDelay` | Уставка должна держаться столько времени |
| `severity` | `Info`, `Warning`, `Alarm`, `Trip` |
| `enabled` | По умолчанию `true` |
| `name`, `message`, `tags` | Необязательные метаданные |

Уровни Warning/Alarm/Trip — это **несколько уставок** на один параметр.
Так проще менять только один уровень и хранить историю.

## 15.3. Fluent-конфигурация

```csharp
using Mvk.MVK01.Setpoints;

MvkSetpointSet setpoints = MvkSetpointSet.Create()
    .Add("mvk-1", Mvk01MeasurementNumber.Rms10To1000Acceleration, channel: 1)
        .Warning(4.5, hysteresis: 0.2)
        .Alarm(7.1, hysteresis: 0.3)
        .Trip(11.0, hysteresis: 0.5);

await using Mvk01AlarmService alarms = await Mvk01AlarmService.StartAsync(
    "mvkmodbus.json",
    setpoints);

await alarms.RunAsync(OnAlarm, cts.Token);
```

Другие правила:

```csharp
MvkSetpointSet setpoints = MvkSetpointSet.Create()
    .Add("mvk-1", Mvk01MeasurementNumber.Rms10To1000Acceleration, 1)
        .Below(0.5, MvkAlarmSeverity.Warning, hysteresis: 0.05)
        .Outside(0.5, 8.0, MvkAlarmSeverity.Alarm, hysteresis: 0.2)
        .Inside(2.0, 3.0, MvkAlarmSeverity.Info);
```

`MvkSetpointSet` можно сохранить в JSON через `JsonMvkSetpointStore` и загрузить
обратно — fluent и файл используют одну модель.

## 15.4. Гистерезис

Гистерезис задаётся в **единицах измерения** уставки.

Для `Above`, порог `4.5`, гистерезис `0.2`:

| Значение | Состояние |
|---|---|
| `4.6` | `Pending`, затем `Active` |
| `4.4` | всё ещё `Active`: выше `4.5 − 0.2 = 4.3` |
| `4.2` | `Normal` — тревога снята |

Для `Below` — зеркально:

| Значение | Состояние |
|---|---|
| `0.9` при пороге `1.0` | `Active` |
| `1.05` при гистерезисе `0.1` | всё ещё `Active` |
| `1.1` | `Normal` |

Для `Outside`/`Inside` гистерезис сужает/расширяет границы диапазона.
Если гистерезис `Outside` больше половины ширины диапазона, уставка не сможет
сняться — валидатор отклонит такую конфигурацию.

`ActivationDelay` защищает от коротких выбросов:

| Время | Значение | Состояние |
|---|---|---|
| `0 мс` | `5.0` | `Pending` |
| `500 мс` | `5.0` | `Pending` |
| `1000 мс` | `5.0` | `Active` + событие |
| `1000 мс` | `4.0` | `Normal` + событие снятия |

## 15.5. Состояния и события

```csharp
public enum MvkSetpointState
{
    Normal,
    Pending,
    Active,
    Unknown
}
```

- `Normal` — значение в норме.
- `Pending` — условие выполнено, идёт `ActivationDelay`.
- `Active` — тревога.
- `Unknown` — нет достоверных данных: ошибка связи или `NaN`.

Событие `MvkAlarmEvent` приходит только при переходе:

- `Normal → Active` — тревога;
- `Active → Normal` — снятие;
- `Normal/Active → Unknown` — потеря данных;
- `Unknown → Normal/Active` — данные вернулись.

Повторные сэмплы с тем же состоянием событий не создают.

```csharp
await foreach (MvkAlarmEvent alarm in alarms.WatchAsync(ct))
{
    Console.WriteLine(
        $"{alarm.DeviceId} {alarm.Setpoint.Id}: "
        + $"{alarm.PreviousState} -> {alarm.State} = {alarm.Value:F3}");
}
```

`MvkAlarmEvent` содержит `DeviceId`, `Setpoint`, `State`, `PreviousState`,
`Value`, `Error`, `Message`, `Timestamp`. Свойства `IsActive`/`IsClear`
упрощают фильтрацию.

## 15.6. Реакции

Основной способ — `RunAsync`:

```csharp
await alarms.RunAsync(async (alarm, token) =>
{
    if (alarm.State == MvkSetpointState.Active)
    {
        await notifier.SendAsync(alarm, token);
    }
}, cancellationToken);
```

Ошибка обработчика не останавливает мониторинг: она логируется, следующее
событие продолжает обрабатываться. Дополнительно её можно получить через
`Mvk01AlarmOptions.OnReactionError` — например, записать в свой журнал.

Для веб-панели или WPF используйте `WatchAsync` и передавайте события в
`Channel<T>`, SignalR или UI-поток. Не вызывайте методы устройства внутри
синхронных обработчиков состояния — это может заблокировать пул.

## 15.7. DI и ASP.NET Core

```csharp
builder.Services.AddMvk01(builder.Configuration);       // AddMvkModbus + AddMvk01Devices
builder.Services.AddMvk01Alarms(options =>
{
    options.SetpointsFile = "mvk-setpoints.json";
    options.PollInterval = TimeSpan.FromMilliseconds(500);
});

// ...

app.MapGet("/api/alarms/stream", async (
    IMvk01AlarmService alarms,
    CancellationToken cancellationToken) =>
{
    return Results.Stream(async stream =>
    {
        await using StreamWriter writer = new(stream);

        await foreach (MvkAlarmEvent alarm in alarms.WatchAsync(cancellationToken))
        {
            string json = JsonSerializer.Serialize(new
            {
                timestamp = alarm.Timestamp,
                device = alarm.DeviceId,
                setpoint = alarm.Setpoint.Id,
                state = alarm.State.ToString(),
                value = float.IsFinite(alarm.Value) ? alarm.Value : (float?)null,
                error = alarm.Error?.ErrorKind.ToString()
            });

            await writer.WriteAsync($"data: {json}\n\n".AsMemory(), cancellationToken);
            await writer.FlushAsync();
        }
    }, "text/event-stream");
});
```

`AddMvk01Alarms` регистрирует JSON-хранилище по умолчанию. Своё хранилище
(например, SQLite) регистрируется до вызова:

```csharp
builder.Services.AddSingleton<IMvkSetpointStore, MySqliteSetpointStore>();
builder.Services.AddMvk01Alarms();
```

`TryAddSingleton` не перезапишет вашу реализацию.

## 15.8. Состояния для UI

```csharp
foreach (MvkSetpointStatus status in alarms.Statuses)
{
    Console.WriteLine(
        $"{status.Setpoint.Name}: {status.State} "
        + $"{status.LastValue:F3} ({status.Timestamp:HH:mm:ss})");
}
```

`MvkSetpointStatus` содержит уставку, состояние, последнее значение, время,
`ActiveSince` и ошибку. `StartAsync` загружает уставки сразу; в DI-сценарии до
первого `WatchAsync`/`ReloadSetpointsAsync` список может быть пуст.

## 15.9. Перезагрузка уставок

```csharp
await alarms.ReloadSetpointsAsync();
```

Перечитывает хранилище и пересоздаёт движок. Разрешено только при
остановленном наблюдении: иначе изменится набор опрашиваемых параметров, а
активный watcher продолжит старый набор. Остановите `WatchAsync`/`RunAsync`,
затем перезагрузите.

## 15.10. Ограничения первой версии

- Только **автосброс** с гистерезисом. Защёлка и квитирование — следующий этап.
- `Unknown` при потере связи не считается `Active`; реакция сама решает,
  нужно ли поднимать тревогу.
- История тревог не хранится в библиотеке: пишите `MvkAlarmEvent` в журнал/БД.
- Один активный `WatchAsync` на сервис.
- Внутренний канал сэмплов ограничен 256 элементами: очень медленный потребитель
  создаёт backpressure и может замедлить опрос. Для больших нагрузок используйте
  собственный монитор с нужным размером буфера.
- Уставки не записываются в МВК01 и не требуют `AllowConfigurationWrite`.
- Полный снимок канала (спектр, напряжение, состояние) уставками не покрывается:
  они работают с параметрами по номерам.

## 15.11. Тестирование

Движок `MvkSetpointEngine` не зависит от Modbus. Его можно проверить на
`Mvk01Sample` без оборудования:

```csharp
MvkSetpointEngine engine = new([setpoint]);
Mvk01Sample sample = new()
{
    Timestamp = DateTimeOffset.UtcNow,
    DeviceId = "mvk-1",
    Item = new Mvk01PollItem(Mvk01MeasurementNumber.Rms10To1000Acceleration, 1),
    Values = [5.0f, 0, 0, 0, 0, 0, 0, 0]
};

IReadOnlyList<MvkAlarmEvent> events = engine.Evaluate(sample, sample.Timestamp);
```

В репозитории есть тесты на гистерезис, задержку, несколько уровней, `Unknown`,
JSON round-trip, fluent-набор, DI и высокоуровневый сервис.

## См. также

- [Фасад МВК01](07b-mvk01-device-facade.md) — `IMvk01Device`, `Mvk01Watcher`.
- [Ошибки, логирование и метрики](08-errors-logging-metrics.md) — классификация ошибок.
- [Рецепты](11-recipes.md) — прикладные сценарии.
- [Справочник Mvk.MVK01](12b-api-reference-mvk-mvk01.md) — типы и сигнатуры.
