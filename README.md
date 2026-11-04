# ConnectMVKBulder

[![CI](https://github.com/Ivan255Mhz/ConnectMVKBulder/actions/workflows/ci.yml/badge.svg)](https://github.com/Ivan255Mhz/ConnectMVKBulder/actions/workflows/ci.yml)

Библиотека-обёртка для устройства **МВК01** (многоканальный виброконтроллер, Modbus/TCP)
на C#/.NET 8. Берёт на себя транспорт, пул устройств, конфигурацию, проверки и декодирование
регистров, оставляя прикладному коду простые типизированные вызовы.

**Документация:** [полное руководство](docs/README.md) — главы по возрастанию сложности,
от первого подключения до собственного транспорта и справочника API.

## Возможности

- **Транспорт Modbus/TCP** с таймаутами, повторными попытками, реконнектом и защитой от записи.
- **Пул устройств**: параллельная работа с десятками МВК01, состояния связи и события,
  автоматическое переподключение, idle-отключение.
- **Пайплайн middleware**: валидация, retry, логирование, метрики, свои обработчики.
- **Типизированный слой МВК01**: снимки каналов и тахометров, измерения по номерам,
  спектральные полосы, системные команды, заводские настройки.
- **Компактный API**: `ReadValueAsync`, `ReadValuesAsync`, `WatchAsync` и
  `AddMvk01` — типовые операции в одну строку.
- **Наблюдение**: `Mvk01Watcher` с `IAsyncEnumerable<Mvk01Sample>` — периодический
  опрос без ручных циклов, ошибки приходят в поток и не прерывают наблюдение.
- **Уставки и тревоги**: прикладные пороги в JSON или fluent-коде, гистерезис,
  задержка срабатывания, уровни Warning/Alarm/Trip и поток событий.
- **Конфигурация**: секция `MvkModbus` (JSON, переменные окружения, DI) с валидацией и наследованием.
- **Тестирование без железа**: эмулятор Modbus-slave, молчащий сервер, программируемый транспорт.
- **242 теста**: транспорт, пайплайн, пул, карта регистров, кодеки, фасад, наблюдение,
  безопасность записи, уставки и гистерезис.

## Состав решения

| Проект | Назначение |
|---|---|
| `src/Mvk.Modbus` | Конфигурация, транспорт Modbus/TCP, пайплайн middleware, пул устройств, DI |
| `src/Mvk.MVK01` | Типизированная карта регистров МВК01, кодеки, фасад устройства, `Mvk01Connection`, watcher и уставки |
| `tests/Mvk.Testing` | Эмулятор Modbus-slave, молчащий сервер и программируемый транспорт для тестов |
| `tests/Mvk.Modbus.Tests` | Тесты транспорта, пайплайна, пула и конфигурации |
| `tests/Mvk.MVK01.Tests` | Тесты карты регистров, кодеков, фасада, watcher’а и уставок |
| `samples/Mvk.Sample.Console` | Пример: DI, свой middleware, обзор устройства и режим `watch` |
| `samples/Mvk.Sample.Polling` | Минимальный пример: опрос СКЗ 10…1000 Гц через `Mvk01Watcher` каждые 0,5 с |
| `samples/Mvk.Sample.Alarms` | Пример: JSON-уставки, `Mvk01AlarmService` и реакции на тревоги |

Зависимости: [NModbus 3.0.81](https://www.nuget.org/packages/NModbus) (изолирован внутри `Mvk.Modbus`),
`Microsoft.Extensions.*`, `System.Text.Encoding.CodePages` (кодовая страница 1251 в `Mvk.MVK01`).

## Требования и сборка

- .NET SDK 8.0+;
- реальное устройство МВК01 в сети — только для ручных smoke-проверок
  (автотесты работают на эмуляторе).

```powershell
dotnet build ConnectMVKBulder.sln
dotnet test
dotnet run --project samples\Mvk.Sample.Console
dotnet run --project samples\Mvk.Sample.Polling
dotnet run --project samples\Mvk.Sample.Alarms
dotnet pack ConnectMVKBulder.sln -c Release -o nupkgs
```

## Установка

Пакеты `Mvk.Modbus` и `Mvk.MVK01`:

```powershell
dotnet add package Mvk.Modbus
dotnet add package Mvk.MVK01
```

До публикации на nuget.org — локальный фид из папки `nupkgs`:

```powershell
dotnet nuget add source .\nupkgs -n mvk-local
dotnet add package Mvk.Modbus
```

Подробнее — глава [«Упаковка и использование в других проектах»](docs/14-packaging.md).

## Быстрый старт

### 1. Одно устройство — без конфигурации

```csharp
using Mvk.MVK01;

await using Mvk01Connection mvk = await Mvk01Connection.ConnectAsync("10.20.0.211");
// порт 502, SlaveId 1, BigEndian, таймауты и реконнект — по умолчанию

Mvk01DeviceInfo info = await mvk.ReadDeviceInfoAsync();
Console.WriteLine($"ПО: {info.FirmwareVersion}, серийный: {info.SerialNumber}");

Mvk01Channel channel = await mvk.ReadChannelAsync(1);
Console.WriteLine($"Напряжение: {channel.SensorVoltage:F4} В, СКЗ X…25кГц: {channel.RmsX25kHzAcceleration:F3} м/с2");

float rms = await mvk.ReadValueAsync(
    Mvk01MeasurementNumber.Rms10To1000Acceleration,
    channel: 1);
Console.WriteLine($"СКЗ 10…1000 Гц: {rms:F3} м/с2");
```

`ConnectAsync` сразу проверяет связь: если устройство недоступно — выбрасывается
`MvkModbusException` с категорией `Timeout`/`Transport`.

> По умолчанию `Mvk01Connection` работает **только на чтение**. Для `Save`, `Reboot`
> и `Restore` используйте `ConnectAsync(..., allowConfigurationWrite: true)`.

### 2. Несколько устройств из JSON

```json
{
  "MvkModbus": {
    "Devices": [
      { "Id": "mvk-1", "Host": "10.20.0.211" }
    ]
  }
}
```

```csharp
using Mvk.Modbus.Pool;

await using MvkDevicePool pool = MvkDevicePool.FromJsonFile("mvkmodbus.json");
IMvkDevice device = pool.GetDevice("mvk-1");

ushort[] registers = await device.ReadHoldingRegistersAsync(0x1004, 2);
```

### 3. Через DI (службы, WPF-приложение, хост)

```csharp
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "mvkmodbus.json"), optional: false);

builder.Services.AddMvk01(builder.Configuration);       // AddMvkModbus + AddMvk01Devices
builder.Services.AddMvkModbusHostedService();           // лог состава пула при старте

using IHost host = builder.Build();
await host.StartAsync();

IMvk01DeviceFactory factory = host.Services.GetRequiredService<IMvk01DeviceFactory>();
Mvk01Channel channel = await factory.Devices.First().ReadChannelAsync(1);
```

> `AddMvk01` — это `AddMvkModbus` + `AddMvk01Devices` одним вызовом.
> `AddMvk01Devices` включает обязательную для МВК01 настройку чётности адреса и
> количества регистров. Запись конфигурации по умолчанию запрещена: включите
> `Defaults.AllowConfigurationWrite = true` явно.

### 4. Периодический опрос

```csharp
using Mvk.MVK01;

IMvk01Device device = factory.Devices.First();

await foreach (var sample in device.WatchAsync(
    Mvk01MeasurementNumber.Rms10To1000Acceleration,
    channel: 1,
    interval: TimeSpan.FromMilliseconds(500),
    cancellationToken))
{
    if (sample.Error is { } error)
    {
        Console.WriteLine($"ошибка: {error.ErrorKind} — {error.Message}");
        continue;
    }

    Console.WriteLine($"СКЗ 10…1000 Гц: {sample.ChannelValue:F4} м/с2");
}
```

Первый опрос выполняется сразу; ошибки приходят в `sample.Error` и не прерывают
наблюдение; отмена — через `cancellationToken`.

### 5. Уставки и тревоги

```csharp
using Mvk.MVK01.Setpoints;

await using Mvk01AlarmService alarms = await Mvk01AlarmService.StartAsync(
    "mvkmodbus.json",
    "mvk-setpoints.json");

await alarms.RunAsync((alarm, _) =>
{
    Console.WriteLine(
        $"{alarm.DeviceId} {alarm.Setpoint.Name}: "
        + $"{alarm.PreviousState} -> {alarm.State} = {alarm.Value:F3}");

    return ValueTask.CompletedTask;
}, cancellationToken);
```

Пороги хранятся в приложении, а не в МВК01. Уставка задаёт параметр, канал,
порог, гистерезис, задержку и важность. События приходят только при переходах
`Normal → Active`, `Active → Normal` и `Unknown`.

## Документация

| № | Глава | Уровень |
|---|---|---|
| 01 | [Введение и обзор](docs/01-introduction.md) | базовый |
| 02 | [Первое подключение](docs/02-getting-started.md) | базовый |
| 03 | [Конфигурация](docs/03-configuration.md) | базовый |
| 04 | [Транспорт и сырые регистры](docs/04-transport-and-raw-registers.md) | средний |
| 05 | [Пул устройств](docs/05-device-pool.md) | средний |
| 06 | [Пайплайн middleware](docs/06-pipeline-middleware.md) | продвинутый |
| 07a | [Карта регистров и кодеки МВК01](docs/07a-mvk01-register-map-and-codec.md) | справочный |
| 07b | [Фасад МВК01](docs/07b-mvk01-device-facade.md) | средний |
| 08 | [Ошибки, логирование и метрики](docs/08-errors-logging-metrics.md) | средний |
| 09 | [Тестирование без железа](docs/09-testing.md) | средний |
| 10 | [Расширение и интеграция](docs/10-extensibility.md) | продвинутый |
| 11 | [Рецепты](docs/11-recipes.md) | практический |
| 12a | [Справочник API: Mvk.Modbus](docs/12a-api-reference-mvk-modbus.md) | справочный |
| 12b | [Справочник API: Mvk.MVK01](docs/12b-api-reference-mvk-mvk01.md) | справочный |
| 13 | [Диагностика и FAQ](docs/13-troubleshooting-faq.md) | справочный |
| 14 | [Упаковка и публикация](docs/14-packaging.md) | практический |
| 15 | [Уставки и тревоги](docs/15-setpoints.md) | практический |

Первоисточник карты регистров устройства: [MVK01_registers.txt](MVK01_registers.txt).

## Особенности МВК01

- Только чтение по умолчанию: запись настроек, `Save`, `Reboot` и `Restore` требуют явного
  разрешения в конфигурации.
- Адрес и количество регистров в одном запросе — только чётные (ограничение устройства).
- Строки каналов хранятся с обратным порядком байт внутри 4-байтных слов, кодировка cp1251.
- Найденные расхождения с документацией устройства (например, адрес параметра N=48 = `0x42F0`)
  собраны в [главе 07a](docs/07a-mvk01-register-map-and-codec.md).

## Лицензия

Проект распространяется по лицензии [MIT](LICENSE).
