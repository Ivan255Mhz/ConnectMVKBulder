# ConnectMVKBulder — библиотека работы с МВК01

Библиотека-обёртка для устройства **МВК01** (многоканальный виброконтроллер, Modbus/TCP)
на C#/.NET 8. Берёт на себя транспорт, пул устройств, конфигурацию, проверки и декодирование
регистров, оставляя прикладному коду простые типизированные вызовы.

- **Полное руководство** (главы по возрастанию сложности, все сущности и примеры):
  [docs/README.md](docs/README.md)
- Справочник регистров: [MVK01_registers.txt](MVK01_registers.txt)
- Roadmap и ключевые решения: [PLAN.md](PLAN.md)
- Правила разработки: [AGENTS.md](AGENTS.md)

## Содержание

1. [Состав решения](#состав-решения)
2. [Требования и сборка](#требования-и-сборка)
3. [Быстрый старт](#быстрый-старт)
4. [Конфигурация](#конфигурация)
5. [Пул устройств](#пул-устройств)
6. [Пайплайн middleware](#пайплайн-middleware)
7. [Типизированный слой МВК01](#типизированный-слой-мвк01)
8. [Периодический опрос значений](#периодический-опрос-значений)
9. [Сырые регистры и кодеки](#сырые-регистры-и-кодеки)
10. [Ошибки](#ошибки)
11. [Логирование и метрики](#логирование-и-метрики)
12. [Тестирование без железа](#тестирование-без-железа)
13. [Расширение библиотеки](#расширение-библиотеки)
14. [Особенности МВК01](#особенности-мвк01)
15. [Частые вопросы и ошибки](#частые-вопросы-и-ошибки)

---

## Состав решения

| Проект | Назначение |
|---|---|
| `src/Mvk.Modbus` | Конфигурация, транспорт Modbus/TCP, пайплайн middleware, пул устройств, DI |
| `src/Mvk.MVK01` | Типизированная карта регистров МВК01, кодеки, фасад устройства, `Mvk01Connection` |
| `tests/Mvk.Testing` | Эмулятор Modbus-slave, молчащий сервер и программируемый транспорт для тестов |
| `tests/Mvk.Modbus.Tests` | 123 теста транспорта, пайплайна, пула и конфигурации |
| `tests/Mvk.MVK01.Tests` | 55 тестов карты регистров, кодеков и фасада |
| `samples/Mvk.Sample.Console` | Пример: DI + свой middleware + чтение канала и спектра |
| `samples/Mvk.Sample.Polling` | Минимальный пример: опрос СКЗ 10…1000 Гц в цикле каждые 0,5 с |

Зависимости: [NModbus 3.0.81](https://www.nuget.org/packages/NModbus) (только внутри `Mvk.Modbus`),
`Microsoft.Extensions.*` (DI, Options, Configuration, Logging, Hosting),
`System.Text.Encoding.CodePages` (кодовая страница 1251 в `Mvk.MVK01`).

## Требования и сборка

- .NET SDK 8.0+ (собрано и проверено на SDK 10, рантайм 8.0)
- Реальное устройство МВК01 в сети (для проверок), по умолчанию в примере —
  `10.20.0.211:502`, SlaveId 1

```powershell
dotnet build ConnectMVKBulder.sln
dotnet test
dotnet run --project samples\Mvk.Sample.Console
```

---

## Быстрый старт

### 1. Одно устройство — без конфигурации

Самый простой сценарий: подключиться по адресу и читать декодированные значения.

```csharp
using Mvk.MVK01;

await using Mvk01Connection mvk = await Mvk01Connection.ConnectAsync("10.20.0.211");
// порт 502, SlaveId 1, BigEndian, таймауты и реконнект — по умолчанию

Mvk01DeviceInfo info = await mvk.ReadDeviceInfoAsync();
Console.WriteLine($"ПО: {info.FirmwareVersion}, серийный: {info.SerialNumber}");

Mvk01Channel channel = await mvk.ReadChannelAsync(1);
Console.WriteLine($"Напряжение: {channel.SensorVoltage:F4} В, СКЗ X…25кГц: {channel.RmsX25kHzAcceleration:F3} м/с2");
Console.WriteLine($"Полос спектра: {channel.Spectrum.Length}");
```

`ConnectAsync` сразу проверяет связь: если устройство недоступно — выбрасывается
`MvkModbusException` с категорией `Timeout`/`Transport`.

### 2. Несколько устройств из JSON

```csharp
using Mvk.Modbus.Pool;

MvkModbusOptions options = MvkModbusOptionsLoader.LoadFromJsonFile("mvkmodbus.json");
// либо короче: await using MvkDevicePool pool = MvkDevicePool.FromJsonFile("mvkmodbus.json");

await using MvkDevicePool pool = new(options);
IMvkDevice device = pool.GetDevice("mvk-1");

ushort[] registers = await device.ReadHoldingRegistersAsync(0x1004, 2);
```

Минимальный `mvkmodbus.json` — только обязательные поля, всё остальное по умолчанию:

```json
{
  "MvkModbus": {
    "Devices": [
      { "Id": "mvk-1", "Host": "10.20.0.211" }
    ]
  }
}
```

### 3. Через DI (службы, WPF-приложение, хост)

```csharp
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "mvkmodbus.json"), optional: false);

builder.Services.AddMvkModbus(builder.Configuration);   // пул + конфигурация + валидация
builder.Services.AddMvk01Devices();                     // типизированный слой МВК01
builder.Services.AddMvkModbusHostedService();            // лог состава пула при старте
builder.Services.AddModbusMiddleware<AuditMiddleware>("audit"); // свой middleware

using IHost host = builder.Build();
await host.StartAsync();

IMvk01DeviceFactory factory = host.Services.GetRequiredService<IMvk01DeviceFactory>();
IMvk01Device device = factory.Devices.First();

Mvk01Channel channel = await device.ReadChannelAsync(1);
```

Регистрируется:

- `IMvkDevicePool` / `MvkDevicePool` — singleton;
- `IMvk01DeviceFactory` — singleton (после `AddMvk01Devices()`);
- `IMvkTransportFactory` — `NModbusTransportFactory` (можно заменить своей регистрацией);
- hosted-сервис — `MvkModbusWarmupHostedService` (только при `AddMvkModbusHostedService()`).

> Порядок вызовов: сначала `AddMvkModbus`, затем `AddMvk01Devices`.
> `AddMvk01Devices` включает обязательные для МВК01 настройки (чётные адрес/количество
> регистров и разрешение записи конфигурации).

---

## Конфигурация

Секция верхнего уровня — `MvkModbus`. Настройки по умолчанию (`Defaults`) применяются
ко всем устройствам; устройство может переопределить часть полей.

### Полный пример

```json
{
  "MvkModbus": {
    "Defaults": {
      "Port": 502,
      "SlaveId": 1,
      "ByteOrder": "BigEndian",
      "ConnectTimeoutMs": 3000,
      "ReadTimeoutMs": 1000,
      "WriteTimeoutMs": 3000,
      "MaxRetries": 2,
      "RetryDelayMs": 150,
      "ReconnectBaseDelayMs": 500,
      "ReconnectMaxDelayMs": 30000,
      "IdleDisconnectSeconds": 120,
      "MaxConcurrentRequests": 1,
      "WritePolicy": "ProtectConfigArea",
      "AllowConfigurationWrite": false,
      "ProtectedWriteStartAddress": "0x0000",
      "ProtectedWriteEndAddress": "0x0FFF",
      "RequireEvenAddressAndQuantity": true
    },
    "Pipeline": ["timeout", "logging", "metrics", "validation", "retry", "writeProtection"],
    "Pool": {
      "ReconnectEnabled": true,
      "HealthCheckIntervalSeconds": 15
    },
    "Devices": [
      {
        "Id": "mvk-1",
        "Name": "МВК01 стенд",
        "Host": "10.20.0.211",
        "Port": 502,
        "SlaveId": 1,
        "ByteOrder": "BigEndian",
        "Enabled": true,
        "MaxConcurrentRequests": 1,
        "AllowConfigurationWrite": true,
        "Tags": { "asset": "Стенд" }
      }
    ]
  }
}
```

`ProtectedWriteStartAddress`/`ProtectedWriteEndAddress` в JSON задаются числом или строкой
(`"0x0FFF"`), обработка — стандартным биндером .NET.

### Справочник `Defaults` (и переопределяемых полей устройства)

| Параметр | По умолчанию | Описание |
|---|---|---|
| `Port` | 502 | TCP-порт Modbus |
| `SlaveId` | 1 | Идентификатор устройства Modbus (1–247) |
| `ByteOrder` | `BigEndian` | Порядок байт 32-битных значений: `BigEndian`, `LittleEndian`, `Mixed` |
| `ConnectTimeoutMs` | 3000 | Таймаут подключения |
| `ReadTimeoutMs` | 1000 | Таймаут ожидания ответа на чтение |
| `WriteTimeoutMs` | 3000 | Таймаут ожидания ответа на запись |
| `MaxRetries` | 2 | Число повторов при временных ошибках |
| `RetryDelayMs` | 150 | Базовая задержка между повторами (линейный backoff) |
| `ReconnectBaseDelayMs` | 500 | Базовая задержка реконнекта |
| `ReconnectMaxDelayMs` | 30000 | Максимум экспоненциального backoff реконнекта |
| `IdleDisconnectSeconds` | 120 | Закрывать соединение после простоя; 0 — не закрывать |
| `MaxConcurrentRequests` | 1 | Число одновременных запросов к одному устройству |
| `WritePolicy` | `ProtectConfigArea` | Политика записи: `AllowAll`, `ProtectConfigArea`, `ReadOnly` |
| `AllowConfigurationWrite` | false | Разрешить запись в защищённую область при `ProtectConfigArea` |
| `ProtectedWriteStartAddress` | 0x0000 | Начало защищённой области (включительно) |
| `ProtectedWriteEndAddress` | 0x0FFF | Конец защищённой области (включительно) |
| `RequireEvenAddressAndQuantity` | false | Требовать чётные адрес и количество регистров (для МВК01 — да) |

### Поля устройства `Devices`

| Поле | Обязательное | Описание |
|---|---|---|
| `Id` | да | Уникальный идентификатор в пуле |
| `Host` | да | IP-адрес или DNS-имя |
| `Name` | нет | Отображаемое имя |
| `Port`, `SlaveId`, `ByteOrder` | нет | Переопределяют `Defaults` |
| `Enabled` | нет | Выключенное устройство отвечает ошибкой `DeviceDisabled` |
| `MaxConcurrentRequests` | нет | Переопределяет `Defaults` |
| `AllowConfigurationWrite` | нет | Переопределяет `Defaults` |
| `Tags` | нет | Произвольные метки (доступны через `device.Settings.Tags`) |

### Пул

| Параметр | По умолчанию | Описание |
|---|---|---|
| `ReconnectEnabled` | true | Фоновое обслуживание: реконнект Faulted-устройств, закрытие простаивающих |
| `HealthCheckIntervalSeconds` | 15 | Период фонового цикла обслуживания |

### Порядок middleware `Pipeline`

Массив имён, первый — самый внешний. `null`/отсутствие — порядок по умолчанию
(`timeout, logging, metrics, validation, retry, writeProtection`).
Важно: пустой массив в JSON (`"Pipeline": []`) биндер превращает в `null`, то есть
даёт порядок по умолчанию; полностью отключить пайплайн можно только программно
(`options.Pipeline = []`).

### Валидация

Конфигурация проверяется при старте хоста (`ValidateOnStart`) или при первом обращении
к настройкам: дубли `Id`, пустой `Host`, недопустимые диапазоны — ошибка
`OptionsValidationException` (DI) или `MvkModbusException` с `MvkErrorKind.Configuration`
(автономный загрузчик `MvkModbusOptionsLoader`).

---

## Пул устройств

### `IMvkDevicePool`

| Член | Описание |
|---|---|
| `Options` | Корневая конфигурация `MvkModbusOptions` |
| `Devices` | Все устройства пула (`IReadOnlyCollection<IMvkDevice>`) |
| `DeviceStateChanged` | Событие изменения состояния любого устройства |
| `GetDevice(id)` | Устройство по идентификатору; не найдено — `MvkModbusException` (`DeviceNotFound`) |
| `TryGetDevice(id, out device)` | Безопасный вариант поиска |
| `AddDevice(MvkDeviceOptions)` | Добавить устройство на лету (дубликат `Id` — ошибка) |
| `RemoveDevice(id)` | Удалить и освободить устройство |
| `MaintainAsync(ct)` | Один цикл обслуживания вручную |

Автономные точки входа:

```csharp
MvkDevicePool pool = new(options);                       // со всеми настройками
MvkDevicePool pool = MvkDevicePool.FromJsonFile("mvkmodbus.json");
```

### `IMvkDevice`

| Член | Описание |
|---|---|
| `Settings` | `MvkDeviceSettings` с применёнными defaults (включая `Endpoint`) |
| `State` | `Disconnected` / `Connecting` / `Connected` / `Faulted` |
| `IsConnected` | Соединение установлено |
| `LastError` | Последняя ошибка (сбрасывается при успехе) |
| `LastRequestAt` | Время последнего запроса (UTC) |
| `StateChanged` | Событие перехода состояния |
| `ExecuteAsync(request, ct)` | Запрос через пайплайн; ошибки — в `ModbusResponse.Error` |
| `ExecuteAsync(request, timeout, ct)` | То же с общим таймаутом операции |
| `EnsureConnectedAsync(ct)` | Явно подключиться (проверка доступности) |
| `DisconnectAsync(ct)` | Закрыть соединение |
| `ReadHoldingRegistersAsync(addr, count, ct)` | Чтение 0x03, результат `ushort[]` |
| `ReadInputRegistersAsync(addr, count, ct)` | Чтение 0x04 |
| `WriteSingleRegisterAsync(addr, value, ct)` | Запись 0x06 |
| `WriteMultipleRegistersAsync(addr, data, ct)` | Запись 0x10 |

Разница в контрактах: `ExecuteAsync` **возвращает** ответ (в том числе с ошибкой),
удобные обёртки **выбрасывают** `MvkModbusException` при неуспехе.

```csharp
// без исключений — код проверяет результат
ModbusResponse response = await device.ExecuteAsync(
    ModbusRequest.ReadHoldingRegisters("mvk-1", 0x1004, 2));
if (response.IsSuccess)
{
    ushort[] version = response.Data;
}

// с исключением — код «счастливого пути»
ushort[] data = await device.ReadHoldingRegistersAsync(0x1004, 2);
```

### Состояния и события

```csharp
pool.DeviceStateChanged += (_, args) =>
    Console.WriteLine($"{args.Device.Settings.Id}: {args.OldState} -> {args.NewState} " +
                      $"({args.Error?.Message ?? "без ошибки"})");
```

Переходы: `Disconnected → Connecting → Connected`, при временной ошибке — `Faulted`,
после успешного реконнекта — снова `Connected`.

### Автореконнект и простои

- при транспортной ошибке устройство переходит в `Faulted` и планирует реконнект
  с экспоненциальной задержкой (`ReconnectBaseDelayMs` → `ReconnectMaxDelayMs`);
- фоновый цикл (при `Pool.ReconnectEnabled = true`) каждые `HealthCheckIntervalSeconds`
  восстанавливает соединение и закрывает простаивающие соединения
  (`IdleDisconnectSeconds`);
- задержка между запросами не нужна — запросы к одному устройству сами
  сериализуются (`MaxConcurrentRequests`).

### Пример: фоновый опрос с обработкой ошибок

```csharp
using CancellationTokenSource cts = new();
IMvkDevice device = pool.GetDevice("mvk-1");

while (!cts.IsCancellationRequested)
{
    try
    {
        ushort[] registers = await device.ReadHoldingRegistersAsync(0x1004, 2, cts.Token);
        Console.WriteLine($"ПО: {registers[0]:X4} {registers[1]:X4}");
    }
    catch (MvkModbusException ex) when (ex.IsTransient)
    {
        // Устройство недоступно; реконнект уже идёт в фоне.
        Console.WriteLine($"МВК-1 недоступен: {ex.ErrorKind} — {ex.Message}");
    }
    catch (MvkModbusException ex)
    {
        Console.WriteLine($"Ошибка данных: {ex.ErrorKind} — {ex.Message}");
    }

    await Task.Delay(1000, cts.Token);
}
```

---

## Пайплайн middleware

Каждый запрос к устройству проходит через цепочку обработчиков (как middleware в ASP.NET):
проверки, логирование, метрики, повторы, защита записи и т.д. Терминальный элемент —
транспорт (сокет).

```
IMvkDevice.ExecuteAsync
  └─ timeout → logging → metrics → validation → retry → writeProtection → NModbusTransport
```

### Контракт

```csharp
public delegate Task InvokeModbusDelegate(ModbusRequestContext context, CancellationToken cancellationToken);

public interface IModbusMiddleware
{
    Task InvokeAsync(ModbusRequestContext context, InvokeModbusDelegate next, CancellationToken cancellationToken);
}
```

`ModbusRequestContext` содержит:

| Член | Описание |
|---|---|
| `Settings` | Параметры устройства |
| `Request` | Запрос (`DeviceId`, `Operation`, `StartAddress`, `Quantity`, `WriteData`) |
| `Response` | Ответ; ставит терминальный middleware или при коротком замыкании |
| `Items` | Данные между middleware в рамках запроса |
| `Attempt` | Номер попытки (заполняет `RetryMiddleware`) |
| `Aborted` | Токен отмены вызывающего кода |
| `TryGetItem<T>(key, out value)` | Чтение значения из `Items` |

Middleware может: изменить/проверить запрос, выполнить действие до/после `next`,
заменить токен отмены, коротко замкнуть цепочку (поставить `Response` и не вызывать `next`).

### Встроенные middleware

| Имя | Класс | Что делает |
|---|---|---|
| `timeout` | `TimeoutMiddleware` | Ограничивает общее время операции; таймаут из `Items[MvkContextItems.TimeoutMs]` |
| `logging` | `LoggingMiddleware` | Пишет в `ILogger`: операция, адрес, результат, длительность |
| `metrics` | `MetricsMiddleware` | Публикует метрики `System.Diagnostics.Metrics` |
| `validation` | `ValidationMiddleware` | Проверяет запрос; для МВК01 — чётность адреса и количества |
| `retry` | `RetryMiddleware` | Повторяет только временные ошибки (`IsTransient`) с backoff |
| `writeProtection` | `WriteProtectionMiddleware` | Политика записи: `ReadOnly`, защита области конфигурации |

### Свой middleware

```csharp
using System.Diagnostics;
using Mvk.Modbus.Pipeline;

public sealed class AuditMiddleware : IModbusMiddleware
{
    private readonly ILogger<AuditMiddleware> logger; // внедряется через DI

    public AuditMiddleware(ILogger<AuditMiddleware> logger) => this.logger = logger;

    public async Task InvokeAsync(ModbusRequestContext context, InvokeModbusDelegate next, CancellationToken cancellationToken)
    {
        long startedAt = Stopwatch.GetTimestamp();
        await next(context, cancellationToken);

        logger.LogInformation("{Device} {Operation} 0x{Address:X4} x{Quantity} -> {Result} за {Elapsed:F0} мс",
            context.Settings.Id, context.Request.Operation, context.Request.StartAddress,
            context.Request.Quantity, context.Response?.IsSuccess == true ? "OK" : "ошибка",
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
    }
}
```

Регистрация и подключение по имени в `Pipeline`:

```csharp
services.AddModbusMiddleware<AuditMiddleware>("audit");
// JSON: "Pipeline": ["timeout", "logging", "metrics", "audit", "validation", "retry", "writeProtection"]
```

Таймаут операции «из кода» (например, для команды сохранения конфигурации):

```csharp
ModbusResponse response = await device.ExecuteAsync(request, TimeSpan.FromSeconds(3), ct);
// middleware timeout увидит Items[MvkContextItems.TimeoutMs] = 3000
```

### Ручная сборка пайплайна (без DI)

```csharp
MvkModbusOptions options = MvkModbusOptionsLoader.LoadFromJsonFile("mvkmodbus.json");
MvkDeviceSettings settings = options.ResolveDevices().First();

using NModbusTransport transport = new(settings);   // транспорт одного устройства

MvkPipeline pipeline = new MvkPipelineBuilder()
    .Use(new LoggingMiddleware())
    .Use(new RetryMiddleware())
    .Build(MvkPipeline.TerminalFromTransport(transport));

ModbusRequestContext context = new(
    settings,
    ModbusRequest.ReadHoldingRegisters(settings.Id, 0x1004, 2),
    CancellationToken.None);

ModbusResponse response = await pipeline.ExecuteAsync(context);
```

---

## Типизированный слой МВК01

### `Mvk01Connection` — простая точка входа

| Метод | Описание |
|---|---|
| `ConnectAsync(host, port = 502, slaveId = 1, ct)` | Подключиться и проверить связь |
| `ReadDeviceInfoAsync(ct)` | Общие сведения (серийный, версия, ошибки, наработка) |
| `ReadChannelAsync(channel, ct)` | Полный снимок канала АЦП |
| `ReadChannelConfigAsync(channel, ct)` | Конфигурация канала |
| `ReadTachoAsync(tacho, ct)` | Снимок канала тахометра |
| `ExecuteSystemCommandAsync(command, timeout?, ct)` | Системная команда с ожиданием статуса |
| `SaveConfigurationAsync(ct)` | Сохранить конфигурацию во Flash |
| `Device` | Нижележащий `IMvk01Device` (сырые регистры, события) |
| `DisposeAsync()` | Закрыть соединение и освободить пул |

### `IMvk01Device`

| Метод | Возвращает |
|---|---|
| `ReadDeviceInfoAsync(ct)` | `Mvk01DeviceInfo` |
| `ReadChannelAsync(channel, ct)` | `Mvk01Channel` — снимок канала (5 запросов) |
| `ReadChannelConfigAsync(channel, ct)` | `Mvk01ChannelConfig` |
| `ReadTachoAsync(tacho, ct)` | `Mvk01TachoMeasurement` |
| `ReadMeasurementByNumberAsync(number, ct)` | `float[8]` — параметр по номеру для всех 8 каналов |
| `ReadMeasurementRawByNumberAsync(number, ct)` | `ushort[16]` — то же без декодирования (битовые поля) |
| `ExecuteSystemCommandAsync(command, timeout?, ct)` | `Mvk01CommandResult` |
| `SaveConfigurationAsync(ct)` / `RestoreConfigurationAsync(ct)` / `RebootAsync(ct)` | `Mvk01CommandResult` |
| `EnsureConnectedAsync(ct)` / `DisconnectAsync(ct)` | Управление соединением |

### Снимки значений

**`Mvk01DeviceInfo`**: `SerialNumber`, `FirmwareVersion` (`1.7.20.0`),
`Errors` (`Mvk01SystemErrors` — биты DSP/SD-карта/Modbus/CRC), `Uptime`, `MemoryCardKilobytes`,
`DspMd5`, `Unit1State`/`Unit2State` (`Mvk01UnitState` — зона и события контроля),
`DspIndicators` (`Mvk01DspIndicators`).

**`Mvk01Channel`**:

| Поле | Описание |
|---|---|
| `SensorVoltage` | Напряжение на датчике, В |
| `Spectrum` | 43 полосы дольоктавного спектра |
| `PeakFactor` | Пик-фактор виброускорения X…25 кГц |
| `RmsX25kHzAcceleration` | СКЗ виброускорения X…25 кГц, м/с2 |
| `RmsSets` | 8 наборов `Mvk01RmsSet` (`Name`, `Acceleration`, `Velocity`, `Displacement`) |
| `DcComponent` | Постоянная составляющая, м/с2 |
| `State` | `Mvk01ChannelState` (перегрузка, буферы, DC выше/ниже нормы) |
| `EquivalentPeakValues` / `EquivalentRangeValues` | Эквивалентные пиковые значения и размах (по 24) |

**`Mvk01TachoMeasurement`**: `FrequencyHz`, `AverageIncrementHz`, `SensorError`,
`TimestampSeconds`, `InputLevel`, `Direction` (`BelowMinimum`/`Tacho1ToTacho2`/`Tacho2ToTacho1`),
`TransitionCount`.

**`Mvk01ChannelConfig`**: `ChannelType`, `SensorFactor`, `OverloadThreshold`, `Units`,
`SerialNumber`, `FilterX25kHz` (`Off`/`Hz6400`/`Hz10000`/`Hz15000`), `Filter1LowBand`/`Filter1HighBand`,
`Filter2LowBand`/`Filter2HighBand`, `MaxDcVoltage`, `MinDcVoltage`.

### Системные команды

| Команда | Значение | Статус успеха |
|---|---|---|
| `RestoreConfiguration` | `0x10001` | `Mvk01CommandStatus.Success` (0x00000) |
| `SaveConfiguration` | `0xF0001` | `0x00BAD` — неверная команда, `0xF0BAD` — ошибка Flash |
| `Reboot` | `0xA0001` | при долгом отсутствии статуса — `Timeout` |

```csharp
Mvk01CommandResult result = await device.SaveConfigurationAsync();
if (result.IsSuccess)
{
    Console.WriteLine("Конфигурация сохранена во Flash");
}
else
{
    Console.WriteLine($"Не сохранено: {result.Status} (статус устройства 0x{result.DeviceStatus:X5})");
}
```

Команда выполняется так: запись 2 регистров по адресу `0x0002`, затем опрос статуса
каждые 200 мс до успеха/ошибки/таймаута (по умолчанию 3 с). Для `SaveConfiguration`
это соответствует требованию документации «таймаут ответа увеличить до 3 с».

### Параметры по номерам (раздел 5.1.2)

Все 8 каналов одного параметра читаются одним запросом. Адрес: `0x4000 + (N−1)×16`.

```csharp
float[] rms = await device.ReadMeasurementByNumberAsync(Mvk01MeasurementNumber.RmsX25kHzAcceleration);
Console.WriteLine($"Канал 1: {rms[0]:F4}, канал 8: {rms[7]:F4}");
```

`Mvk01MeasurementNumber` содержит именованные параметры (1–80 и ключевые далее);
для остальных используйте числовой номер (1–184):

```csharp
float[] values = await device.ReadMeasurementByNumberAsync(123);
ushort[] raw = await device.ReadMeasurementRawByNumberAsync(80); // битовое поле состояния
```

### Спектр

```csharp
Mvk01Channel channel = await device.ReadChannelAsync(1);

for (int band = 1; band <= Mvk01SpectrumBands.Count; band++)
{
    float value = channel.GetSpectrumBand(band);
    if (value > 0.001f)
    {
        Console.WriteLine($"{Mvk01SpectrumBands.FormatBand(band),-10} {value:F4}");
    }
}
```

`Mvk01SpectrumBands.CenterFrequencies` — 43 средние частоты от 0.63 Гц до 10 кГц.

### Перечисления

| Тип | Значения |
|---|---|
| `Mvk01Zone` | `Undefined`, `Green`, `SensorFailure`, `InvalidConfiguration`, `Yellow`, `Red` |
| `Mvk01SpectrumDataType` | `Acceleration`, `Velocity`, `Displacement` |
| `Mvk01FilterX25kHz` | `Off`, `Hz6400`, `Hz10000`, `Hz15000` |
| `Mvk01UartModuleType` | `None`, `Kpa1V`, `Vdm82` |
| `Mvk01RotationDirection` | `BelowMinimum`, `Tacho1ToTacho2`, `Tacho2ToTacho1` |

### Карта регистров `Mvk01RegisterMap`

| Область | Адреса | Доступ |
|---|---|---|
| Общие параметры устройства | 0x0000–0x00FF | чтение/запись |
| Конфигурация каналов АЦП (8 блоков по 0x100) | 0x0100–0x08FF | чтение/запись |
| Конфигурация тахометров (2 блока по 0x100) | 0x0900–0x0AFF | чтение/запись |
| Системные параметры | 0x1000–0x1FFF | только чтение |
| Расчётные параметры по каналам (8 блоков по 0x200) | 0x2000–0x2FFF | только чтение |
| Расчётные параметры тахометров (2 блока по 0x100) | 0x3000–0x31FF | только чтение |
| Параметры по номерам (8 каналов × 2 регистра) | 0x4000+ | только чтение |

Основные формулы (все — методы класса):

| Метод | Формула | Пример |
|---|---|---|
| `ChannelConfig(n)` | `0x100 + (n−1)×0x100` | канал 1 → 0x0100 |
| `ChannelData(n)` | `0x2000 + (n−1)×0x200` | канал 8 → 0x2E00 |
| `TachoConfig(n)` | `0x900 + (n−1)×0x100` | тахометр 2 → 0x0A00 |
| `TachoData(n)` | `0x3000 + (n−1)×0x100` | тахометр 1 → 0x3000 |
| `MeasurementByNumber(N)` | `0x4000 + (N−1)×16` | N=48 → 0x42F0 |

Ключевые адреса: `SystemCommand` 0x0002, `ByteOrder` 0x000C, `DeviceSerialNumber` 0x1000,
`FirmwareVersion` 0x1004, `SystemErrorIndicators` 0x1006, `Uptime` 0x1008, `DspMd5` 0x100C,
`SensorVoltageOffset` 0x00, `SpectrumOffset` 0x02, `RmsSetOffset` 0x60, `ChannelStateOffset` 0x9E
(смещения — от базы канала).

---

## Периодический опрос значений

Пример: каждые 0,5 с получать СКЗ виброускорения в полосе 10…1000 Гц по каналу 1.
Опрос с таким периодом для МВК01 безопасен: запросы к устройству сериализуются,
данные обновляются DSP-буфером, повторная отправка не требуется.

### Способ 1 — простой: параметр по номеру (один запрос на все 8 каналов)

```csharp
using Mvk.MVK01;

await using Mvk01Connection mvk = await Mvk01Connection.ConnectAsync("10.20.0.211");
IMvk01Device device = mvk.Device;

using CancellationTokenSource cts = new();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(500));
while (await timer.WaitForNextTickAsync(cts.Token))
{
    try
    {
        float[] values = await device.ReadMeasurementByNumberAsync(
            Mvk01MeasurementNumber.Rms10To1000Acceleration, cts.Token);

        Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  канал 1: {values[0]:F4} м/с2");
    }
    catch (MvkModbusException ex)
    {
        // Транспортные ошибки: реконнект уже идёт в фоне, продолжаем опрос.
        Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  ошибка: {ex.ErrorKind} — {ex.Message}");
    }
}
```

Если нужны все каналы сразу — тот же вызов уже вернул `values[0..7]`.
Номер параметра: `Rms10To1000Acceleration = 55` (см. `Mvk01MeasurementNumber`).

### Способ 2 — минимальный трафик: только 2 регистра канала

Читаем адрес нужной полосы напрямую. Смещение набора СКЗ:
`0x60 + (индекс набора) × 6 + (0 — ускорение, 2 — скорость, 4 — перемещение)`.

| Индекс набора | Полоса | `RmsSet` |
|---|---|---|
| 0 | 10…3000 Гц | `channel.RmsSets[0]` |
| 1 | 10…2000 Гц | `channel.RmsSets[1]` |
| **2** | **10…1000 Гц** | `channel.RmsSets[2]` |
| 3 | 2…1000 Гц | `channel.RmsSets[3]` |
| 4 | 0.8…300 Гц | `channel.RmsSets[4]` |
| 5 | 0.8…150 Гц | `channel.RmsSets[5]` |
| 6 | Фильтр 1 | `channel.RmsSets[6]` |
| 7 | Фильтр 2 | `channel.RmsSets[7]` |

```csharp
using Mvk.Modbus;
using Mvk.Modbus.Protocol;
using Mvk.MVK01;

const int channel = 1;
const int rmsSetIndex = 2; // 10…1000 Гц

ushort address = (ushort)(Mvk01RegisterMap.ChannelData(channel)
    + Mvk01RegisterMap.RmsSetOffset
    + Mvk01RegisterMap.RmsSetValues * 2 * rmsSetIndex);   // 0x206C для канала 1

IMvkDevice raw = mvk.Device.Device;
using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(500));

while (await timer.WaitForNextTickAsync(cts.Token))
{
    try
    {
        ushort[] registers = await raw.ReadHoldingRegistersAsync(address, 2, cts.Token);
        float value = RegisterCodec.ToSingle(registers, raw.Settings.ByteOrder);

        Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  10…1000 Гц: {value:F4} м/с2");
    }
    catch (MvkModbusException ex) when (ex.IsTransient)
    {
        Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  нет связи: {ex.ErrorKind}");
    }
}
```

### Способ 3 — весь снимок канала

Если нужны напряжение, спектр, все СКЗ и состояния — периодически читайте снимок
целиком (5 запросов, единицы–десятки миллисекунд):

```csharp
// mvk и cts — из примера выше
Mvk01Channel snapshot = await mvk.Device.ReadChannelAsync(1, cts.Token);
float value = snapshot.RmsSets[2].Acceleration;   // 10…1000 Гц
```

### Готовый пример в репозитории

Режим `watch` консольного примера опрашивает СКЗ 10…1000 Гц по всем 8 каналам
каждые 0,5 с (без ограничения по времени, остановка — Ctrl+C или аргумент с секундами):

```powershell
dotnet run --project samples\Mvk.Sample.Console -- watch      # до Ctrl+C
dotnet run --project samples\Mvk.Sample.Console -- watch 10   # 10 секунд
```

### Замечания

- Для стабильного периода используйте `PeriodicTimer`, а не `Task.Delay` —
  первый не накапливает дрейф.
- Не запускайте параллельные циклы опроса одного устройства без необходимости:
  запросы и так выполняются по очереди (`MaxConcurrentRequests`).
- Если значение нужно отображать в WPF, обновляйте привязки в UI-потоке
  (`Application.Current.Dispatcher.InvokeAsync`) или используйте `SynchronizationContext`.
- Отмена (`CancellationToken`) проявляется как `OperationCanceledException` —
  в примерах она завершает цикл через `WaitForNextTickAsync`.

## Сырые регистры и кодеки

Для нестандартных задач используйте низкоуровневый доступ:

```csharp
IMvkDevice device = pool.GetDevice("mvk-1");

// чтение/запись регистров
ushort[] registers = await device.ReadHoldingRegistersAsync(0x2000, 2);
await device.WriteMultipleRegistersAsync(0x0100, [0x1111, 0x2222]);

// декодирование с учётом порядка байт устройства
float voltage = RegisterCodec.ToSingle(registers, device.Settings.ByteOrder);
```

`RegisterCodec` (пространство `Mvk.Modbus.Protocol`):

| Метод | Назначение |
|---|---|
| `ToSingle(registers, order)` / `FromSingle(value, order)` | float (2 регистра) |
| `ToUInt32` / `FromUInt32`, `ToInt32` | 32-битные целые |
| `ToText(registers, encoding, byteCount)` / `FromText(text, regCount, encoding)` | строки |
| `ToBytes(registers, byteCount)` | байты (старший байт регистра — первый) |
| `GetBit(value, bitIndex)` | проверка бита |

`MvkByteOrder`: `BigEndian` (по умолчанию, регистр 0x000C = 0x00000000),
`Mixed` (0x99999999, для ВДМ82), `LittleEndian` (0x66666666).

---

## Ошибки

Все ошибки работы с устройством — производные `MvkModbusException` со свойством
`ErrorKind`, `DeviceId` и `ModbusExceptionCode` (для ответов-исключений Modbus).

| `MvkErrorKind` | Когда возникает | `IsTransient` |
|---|---|---|
| `Transport` | Разрыв, недоступность, сбой ввода-вывода | да |
| `Timeout` | Истёк таймаут подключения/ответа | да |
| `Busy` | Устройство занято (постановка в очередь) | да |
| `DeviceError` | Устройство вернуло код ошибки Modbus | нет |
| `Validation` | Нарушение проверок (чётность, политика записи) | нет |
| `DeviceNotFound` | Идентификатора нет в пуле | нет |
| `DeviceDisabled` | `Enabled = false` | нет |
| `Configuration` | Ошибка конфигурации/загрузки | нет |
| `Canceled` | Операция отменена | нет |
| `Unknown` | Прочее | нет |

Рекомендации:

```csharp
try
{
    await device.ReadHoldingRegistersAsync(0x2001, 2, ct);
}
catch (MvkModbusException ex) when (ex.IsTransient)
{
    // связь: повтор/ожидание — реконнект уже выполняется автоматически
}
catch (MvkModbusException ex)
{
    // данные/конфигурация: исправлять запрос или настройки
}
```

Отмена вызывающим кодом (`CancellationToken`) проявляется как `OperationCanceledException`,
а не как `MvkModbusException`.

---

## Логирование и метрики

- Библиотека логирует через `ILogger<T>` (категории `Mvk.Modbus.*`, `Mvk.MVK01.*`).
  Подключите привычные провайдеры (`Console`, `Serilog`, `NLog` и т.д.) — ничего
  дополнительно настраивать не нужно.
- `MetricsMiddleware` публикует метрики через `System.Diagnostics.Metrics`, meter `Mvk.Modbus`:

| Инструмент | Тип | Теги |
|---|---|---|
| `mvk.modbus.requests` | Counter | `device`, `operation`, `result` |
| `mvk.modbus.duration` | Histogram (мс) | `device`, `operation`, `result` |

Пример экспорта:

```csharp
using System.Diagnostics.Metrics;

using MeterListener listener = new();
listener.InstrumentPublished = (instrument, l) =>
{
    if (instrument.Meter.Name == "Mvk.Modbus") l.EnableMeasurementEvents(instrument);
};
listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
    Console.WriteLine($"{instrument.Name}: {value} [{string.Join(", ", tags.ToArray().Select(t => $"{t.Key}={t.Value}"))}]"));
listener.Start();
```

---

## Тестирование без железа

`tests/Mvk.Testing` даёт всё для юнит- и интеграционных тестов:

| Класс | Назначение |
|---|---|
| `MvkTestServer` | Modbus-slave на loopback с регистрами в памяти (NModbus) |
| `SilentTcpServer` | Принимает соединения, но не отвечает (проверка таймаутов) |
| `FakeModbusTransport` / `FakeModbusTransportFactory` | Программируемый транспорт: обработчик ответов, журнал запросов |

```csharp
using Mvk.Testing;

await using MvkTestServer server = MvkTestServer.Start();
server.SetHoldingRegisters(0x1004, 0x0100, 0x4301);

MvkModbusOptions options = new()
{
    Defaults = new MvkDefaultsOptions { Port = server.Port },
    Devices = [new MvkDeviceOptions { Id = "mvk-1", Host = "127.0.0.1" }]
};

await using MvkDevicePool pool = new(options);
IMvkDevice device = pool.GetDevice("mvk-1");

ushort[] data = await device.ReadHoldingRegistersAsync(0x1004, 2);
Assert.Equal(new ushort[] { 0x0100, 0x4301 }, data);
```

Полезные методы `MvkTestServer`: `Start(slaveId, port)`, `GetFreePort()`,
`SetHoldingRegister(s)`, `GetHoldingRegister(s)`, `DisposeAsync()`.

---

## Расширение библиотеки

### Свой транспорт

Реализуйте `IMvkTransport` и свою фабрику — например, для последовательного порта
или прокси:

```csharp
public sealed class MyTransport : IMvkTransport
{
    public bool IsConnected => ...;
    public Task ConnectAsync(CancellationToken ct) => ...;
    public Task DisconnectAsync() => ...;

    public Task<ModbusResponse> ExecuteAsync(ModbusRequest request, CancellationToken ct)
    {
        // вернуть ModbusResponse.Success(data) / WriteSuccess() / Failure(error)
    }

    public void Dispose() { }
}

public sealed class MyTransportFactory : IMvkTransportFactory
{
    public IMvkTransport Create(MvkDeviceSettings settings) => new MyTransport(settings);
}
```

Подключение: в DI — зарегистрируйте `IMvkTransportFactory` после `AddMvkModbus`
(последняя регистрация побеждает) или передайте фабрику в конструктор `MvkDevicePool`.

### Свои middleware

См. раздел [Пайплайн middleware](#пайплайн-middleware): реализуйте `IModbusMiddleware`,
зарегистрируйте `AddModbusMiddleware<T>("имя")`, добавьте имя в `Pipeline`.

---

## Особенности МВК01

Проверено на реальном устройстве (10.20.0.211, ПО 1.0.67.1):

1. **Чётность адреса и количества регистров.** Устройство принимает только запросы
   с чётным адресом и чётным числом регистров; иначе возвращает ошибку Modbus 0x01.
   Включено через `RequireEvenAddressAndQuantity: true` (это делает `AddMvk01Devices()`
   и `Mvk01Connection`).
2. **32-битные значения.** Все параметры — 4 байта (float/uint), порядок задаётся
   регистром `0x000C` (по умолчанию BigEndian).
3. **Адресация по номерам.** `0x4000 + (N−1)×16`; пример для N=48 в
   `MVK01_registers.txt` (0x41F0) ошибочен — фактически **0x42F0**.
4. **Строки каналов.** Единицы измерения и серийные номера датчиков хранятся
   с обратным порядком байт внутри 4-байтных слов (обрабатывает
   `Mvk01Codec.DecodeTextSwapped`), кодировка cp1251. Серийный номер устройства —
   прямой порядок.
5. **Команды.** `SaveConfiguration` может отвечать до 3 секунд — таймаут операции
   подставляется автоматически; на реальном устройстве не выполняйте Save/Reboot
   без явного разрешения.

---

## Частые вопросы и ошибки

**`Middleware 'X' не найден`** — имя в `Pipeline` не совпало ни со встроенным, ни с
зарегистрированным через `AddModbusMiddleware<T>("X")`. Имена нечувствительны к регистру.

**`OptionsValidationException` при старте** — проверьте `Id`, `Host`, диапазоны порта и
таймаутов: валидатор вернёт список всех нарушений.

**`Устройство 'X' не найдено в конфигурации пула`** — `GetDevice` вызывается с
идентификатором, которого нет в `Devices`; используйте `TryGetDevice` или `AddDevice`.

**`Устройство 'X' отключено в конфигурации`** — `Enabled = false`; включите устройство
или не опрашивайте его.

**`Запись в защищённую область конфигурации ... запрещена`** — `WritePolicy` =
`ProtectConfigArea` и `AllowConfigurationWrite = false`. Разрешите запись для нужного
устройства или выполняйте запись через типизированные команды (в DI это уже разрешено
после `AddMvk01Devices`).

**Запрос завершается `Timeout` на медленных операциях** — увеличьте `ReadTimeoutMs`
или передайте общий таймаут в `ExecuteAsync(request, timeout, ct)`.

**Как прочитать все 8 каналов одного параметра?** — `ReadMeasurementByNumberAsync(number)`
(один запрос, 16 регистров).

**Где смотреть адреса?** — `Mvk01RegisterMap` (константы и формулы) и
[MVK01_registers.txt](MVK01_registers.txt) (первоисточник).

**Сборка/тесты/пример:**

```powershell
dotnet build ConnectMVKBulder.sln
dotnet test
dotnet run --project samples\Mvk.Sample.Console
```
