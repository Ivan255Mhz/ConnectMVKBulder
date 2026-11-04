# 07b. Фасад устройства МВК01

Глава описывает высокоуровневый API библиотеки — типизированный фасад МВК01:
`Mvk01Connection`, `IMvk01Device`/`Mvk01Device`, снимки значений (`Mvk01DeviceInfo`,
`Mvk01Channel`, `Mvk01TachoMeasurement`, `Mvk01ChannelConfig`), системные команды,
чтение параметров по номерам и регистрация в DI (`IMvk01DeviceFactory`,
`AddMvk01Devices`). Это тот слой, ради которого библиотека и создавалась: прикладной
код получает декодированные значения одним-двумя вызовами, без ручных
`ReadHoldingRegistersAsync` и `BitConverter`.

Нижний слой — адресация и кодеки — описан в
[главе 7a](07a-mvk01-register-map-and-codec.md); транспорт, пул и middleware —
в [главе 4](04-transport-and-raw-registers.md), [главе 5](05-device-pool.md)
и [главе 6](06-pipeline-middleware.md). Поведение проверено на реальном устройстве
10.20.0.211 (ПО 1.0.67.1).

Содержание:

1. [Mvk01Connection — простейший вход](#1-mvk01connection--простейший-вход)
2. [IMvk01Device и Mvk01Device](#2-imvk01device-и-mvk01device)
3. [Снимки значений](#3-снимки-значений)
4. [ReadChannelAsync подробно](#4-readchannelasync-подробно)
5. [Системные команды](#5-системные-команды)
6. [Параметры по номерам](#6-параметры-по-номерам)
7. [Фабрика фасадов и DI](#7-фабрика-фасадов-и-di)
8. [Производительность и паттерны](#8-производительность-и-паттерны)
9. [Обработка ошибок](#9-обработка-ошибок)
10. [Примеры](#10-примеры)
11. [Подводные камни](#подводные-камни)
12. [См. также](#см-также)

---

## 1. Mvk01Connection — простейший вход

`Mvk01Connection` — это один класс для сценария «одно устройство по адресу,
без конфигурации». Внутри он создаёт пул (`MvkDevicePool`) с одним устройством,
подключается и отдаёт готовый `IMvk01Device`.

```csharp
public sealed class Mvk01Connection : IAsyncDisposable
```

### 1.1 Состав

| Член | Тип | Описание |
|---|---|---|
| `ConnectAsync(host, port = 502, slaveId = 1, ct, allowConfigurationWrite = false)` | `static Task<Mvk01Connection>` | Подключиться и проверить связь; запись конфигурации по умолчанию запрещена |
| `Device` | `IMvk01Device` | Типизированное устройство (расширенный API и события) |
| `ReadDeviceInfoAsync(ct)` | `Task<Mvk01DeviceInfo>` | Общие сведения об устройстве |
| `ReadChannelAsync(channel, ct)` | `Task<Mvk01Channel>` | Полный снимок канала АЦП |
| `ReadChannelConfigAsync(channel, ct)` | `Task<Mvk01ChannelConfig>` | Конфигурация канала АЦП |
| `ReadTachoAsync(tacho, ct)` | `Task<Mvk01TachoMeasurement>` | Снимок канала тахометра |
| `ExecuteSystemCommandAsync(command, timeout?, ct)` | `Task<Mvk01CommandResult>` | Системная команда с ожиданием статуса |
| `SaveConfigurationAsync(ct)` | `Task<Mvk01CommandResult>` | Сохранить конфигурацию во Flash |
| `DisposeAsync()` | `ValueTask` | Закрыть соединение и освободить пул |

Обратите внимание: у `Mvk01Connection` нет методов `ReadMeasurementByNumberAsync`,
`ReadMeasurementRawByNumberAsync`, `RestoreConfigurationAsync` и `RebootAsync` —
они доступны через свойство `Device` (см. раздел 2).

### 1.2 Что делает ConnectAsync

Сигнатура:

```csharp
public static async Task<Mvk01Connection> ConnectAsync(
    string host,
    int port = 502,
    byte slaveId = 1,
    CancellationToken cancellationToken = default,
    bool allowConfigurationWrite = false)
```

Алгоритм:

1. Проверяет `host`: `null`, пустая строка или пробелы — `ArgumentException`.
2. Создаёт `MvkModbusOptions` с настройками:

   | Настройка | Значение | Зачем |
   |---|---|---|
   | `Defaults.Port` | `port` (по умолчанию 502) | TCP-порт Modbus |
   | `Defaults.SlaveId` | `slaveId` (по умолчанию 1) | Идентификатор Modbus |
   | `Defaults.RequireEvenAddressAndQuantity` | `true` | МВК01 принимает только чётные адрес и количество регистров |
   | `Defaults.AllowConfigurationWrite` | `allowConfigurationWrite` (по умолчанию `false`) | Запись в конфигурацию и системные команды — только при явном разрешении |
   | `Pool.ReconnectEnabled` | `true` | Фоновый реконнект при обрыве |
   | `Devices[0].Id/Name/Host` | `host.Trim()` | Идентификатор устройства — сам адрес |

   Остальные параметры (BigEndian, таймауты, ретраи, `MaxConcurrentRequests = 1`)
   берутся из значений по умолчанию — см. [главу 3](03-configuration.md).
3. Создаёт `MvkDevicePool` (при `ReconnectEnabled = true` стартует фоновое обслуживание).
4. Получает устройство пула по `Id` и оборачивает его в `Mvk01Device`
   (кодировка строк по умолчанию — cp1251).
5. Вызывает `EnsureConnectedAsync(cancellationToken)` — связь проверяется **сразу**.
6. При любой ошибке на шагах 3–5 освобождает пул (`await pool.DisposeAsync()`)
   и пробрасывает исключение дальше. Утечки сокетов и фоновых задач нет.

Исключение при неудачном подключении — `MvkModbusException` с категорией
`Timeout` или `Transport` (проверьте адрес, доступность порта 502 и питание устройства).

### 1.3 Полный пример консольного приложения

```csharp
using System;
using System.Threading.Tasks;
using Mvk.MVK01;

internal static class Program
{
    private static async Task Main()
    {
        await using Mvk01Connection mvk = await Mvk01Connection.ConnectAsync("10.20.0.211");
        // порт 502, SlaveId 1, BigEndian, реконнект — по умолчанию

        IMvk01Device device = mvk.Device;
        Console.WriteLine($"Устройство: {device.Id} ({device.Settings.Endpoint}), состояние: {device.State}");

        Mvk01DeviceInfo info = await device.ReadDeviceInfoAsync();
        Console.WriteLine($"Серийный номер: {info.SerialNumber}");
        Console.WriteLine($"ПО: {info.FirmwareVersion}, наработка: {info.Uptime}");
        Console.WriteLine($"Карта памяти: {info.MemoryCardKilobytes} КБ, "
            + $"ошибки: 0x{info.Errors.Raw:X8} (чисто: {info.Errors.IsClean})");
        Console.WriteLine($"Агрегат 1: {info.Unit1State.Zone}, агрегат 2: {info.Unit2State.Zone}");

        Mvk01Channel channel = await device.ReadChannelAsync(1);
        Console.WriteLine();
        Console.WriteLine($"Канал 1: напряжение {channel.SensorVoltage:F2} В, "
            + $"пик-фактор {channel.PeakFactor:F2}, СКЗ X…25 кГц {channel.RmsX25kHzAcceleration:F3} м/с2");
        Console.WriteLine($"  СКЗ 10…1000 Гц: {channel.RmsSets[2].Acceleration:F3} м/с2");
        Console.WriteLine($"  Полос спектра: {channel.Spectrum.Length}");

        Mvk01TachoMeasurement tacho = await device.ReadTachoAsync(1);
        Console.WriteLine();
        Console.WriteLine($"Тахометр 1: {tacho.FrequencyHz:F2} Гц, "
            + $"направление {tacho.Direction}, переходов {tacho.TransitionCount}");
    }
}
```

Пример вывода (числа — из замеров на стенде и тестовых сценариев;
культура ru-RU):

```
Устройство: 10.20.0.211 (10.20.0.211:502), состояние: Connected
Серийный номер: 00004730
ПО: 1.0.67.1, наработка: 1.20:54:13
Карта памяти: 1024 КБ, ошибки: 0x00000000 (чисто: True)
Агрегат 1: Green, агрегат 2: Green

Канал 1: напряжение 7,82 В, пик-фактор 3,50, СКЗ X…25 кГц 12,250 м/с2
  СКЗ 10…1000 Гц: 7,000 м/с2
  Полос спектра: 43

Тахометр 1: 17,14 Гц, направление BelowMinimum, переходов 12345
```

`await using` обязателен: `DisposeAsync` закрывает сокет и останавливает фоновый
цикл обслуживания пула. Если приложение завершается через `Ctrl+C`, `await using`
может не успеть выполниться — обработайте `Console.CancelKeyPress` (см. пример
в `samples\Mvk.Sample.Console`).

### 1.4 Ограничения простого входа

- **Нет чтения по номерам.** `ReadMeasurementByNumberAsync` вызывается через
  `mvk.Device.<...>` — свойство `Device` даёт полный `IMvk01Device`.
- **Идентификатор — адрес.** Два устройства с одинаковым IP, но разными портами
  или `SlaveId` так подключить нельзя: `Id` = `host.Trim()`. Нужна конфигурация
  с разными `Id` и пул/DI (см. [главу 3](03-configuration.md)).
- **Подключение немедленное.** `ConnectAsync` не «ленивый»: недоступное устройство —
  сразу `MvkModbusException`. Цикл ожидания пишите сами либо используйте пул,
  который реконнектится в фоне.

---

## 2. IMvk01Device и Mvk01Device

### 2.1 Интерфейс

```csharp
public interface IMvk01Device
```

| Член | Что делает | Исключения |
|---|---|---|
| `Id` | Идентификатор из конфигурации (`string`) | — |
| `Settings` | `MvkDeviceSettings`: `Host`, `Port`, `SlaveId`, `ByteOrder`, `Endpoint`, `Tags` и т.д. | — |
| `State` | `MvkDeviceState`: `Disconnected`/`Connecting`/`Connected`/`Faulted` | — |
| `Device` | Сырое `IMvkDevice` (регистры, `ExecuteAsync`, `StateChanged`) | — |
| `EnsureConnectedAsync(ct)` | Явно подключиться | `MvkModbusException` |
| `DisconnectAsync(ct)` | Закрыть соединение | `MvkModbusException` |
| `ReadDeviceInfoAsync(ct)` | 3 запроса: `0x1000×12`, `0x100C×12`, `0x1020×6` | `MvkModbusException` |
| `ReadChannelAsync(channel, ct)` | 5 запросов, полный снимок канала (раздел 4) | `MvkModbusException`, `ArgumentOutOfRangeException` |
| `ReadChannelConfigAsync(channel, ct)` | 5 запросов: `A×6`, `A+0x10×16`, `A+0x20×16`, `A+0x3E×10`, `A+0x50×4` | `MvkModbusException`, `ArgumentOutOfRangeException` |
| `ReadTachoAsync(tacho, ct)` | 1 запрос: `TachoData(tacho)×14` | `MvkModbusException`, `ArgumentOutOfRangeException` |
| `ReadMeasurementByNumberAsync(number, ct)` | 1 запрос: 16 регистров от `0x4000 + (N−1)×16`, декодирует `float[8]` | `MvkModbusException`, `ArgumentOutOfRangeException` |
| `ReadMeasurementByNumberAsync(Mvk01MeasurementNumber, ct)` | То же для именованного номера | То же |
| `ReadMeasurementRawByNumberAsync(number, ct)` | То же, но `ushort[16]` без декодирования | `MvkModbusException`, `ArgumentOutOfRangeException` |
| `ExecuteSystemCommandAsync(command, timeout?, ct)` | Запись команды и ожидание статуса (раздел 5) | `OperationCanceledException`; транспортные ошибки — **в результате** |
| `SaveConfigurationAsync(ct)` | `ExecuteSystemCommandAsync(SaveConfiguration)` | То же |
| `RestoreConfigurationAsync(ct)` | `ExecuteSystemCommandAsync(RestoreConfiguration)` | То же |
| `RebootAsync(ct)` | `ExecuteSystemCommandAsync(Reboot)` | То же |

Диапазоны аргументов: канал `1..8`, тахометр `1..2`, номер параметра `1..184`.
Нарушение — `ArgumentOutOfRangeException` (это ошибка программиста, а не связи;
см. тесты `ReadChannelAsync_InvalidChannel_Throws`,
`ReadTachoAsync_InvalidTacho_Throws`,
`ReadMeasurementByNumberAsync_InvalidNumber_Throws`).

Методы чтения при транспортной ошибке бросают `MvkModbusException` — единственный
тип исключений связи в библиотеке (см. README, раздел «Ошибки»).

### 2.2 Класс Mvk01Device

```csharp
public sealed class Mvk01Device : IMvk01Device
{
    public Mvk01Device(IMvkDevice device, Encoding? textEncoding = null);
}
```

| Параметр | Смысл |
|---|---|
| `device` | Базовое устройство пула; `null` — `ArgumentNullException` |
| `textEncoding` | Кодировка текста устройства (единицы измерения, серийные номера датчиков). `null` — `Mvk01Codec.DefaultTextEncoding`, то есть **cp1251** (с откатом в Latin1, если провайдер кодовых страниц недоступен) |

Кодировка нужна только для строковых полей `Mvk01ChannelConfig.Units` и
`Mvk01ChannelConfig.SerialNumber` (на устройстве они лежат в cp1251 с обратным
порядком байт в 4-байтных словах — декодирует `Mvk01Codec.DecodeTextSwapped`).
Числовые поля от неё не зависят.

Свойство `Device` возвращает то же `IMvkDevice`, что передано в конструктор —
это «аварийный выход» к сырым регистрам, событиям `StateChanged` и таймаутному
`ExecuteAsync(request, timeout, ct)`, когда типизированных методов не хватает:

```csharp
IMvkDevice raw = device.Device;
ushort[] registers = await raw.ReadHoldingRegistersAsync(0x1004, 2, ct);
```

Остальные свойства — делегаты базового устройства: `Id => device.Settings.Id`,
`Settings => device.Settings`, `State => device.State`.

### 2.3 Кто создаёт фасады

| Способ | Что делает | Кодировка |
|---|---|---|
| `new Mvk01Device(pool.GetDevice("mvk-1"))` | Ручное создание поверх устройства пула | cp1251 |
| `Mvk01Connection.ConnectAsync(...)` | Создаёт пул и фасад внутри | cp1251 |
| `IMvk01DeviceFactory.GetDevice(id)` | Кэширующий фасад поверх пула | см. раздел 7 |
| `AddMvk01Devices()` (DI) | Регистрирует фабрику; фасады выдаются ею | см. раздел 7 |

Создавать `Mvk01Device` вручную на каждый вызов не нужно: фасад лёгкий,
но в DI-сценарии для этого есть кэширующая `IMvk01DeviceFactory` (раздел 7).

---

## 3. Снимки значений

Все снимки — неизменяемые `record` с обязательными (`required`) свойствами
(`src/Mvk.MVK01/Mvk01Snapshots.cs`). Это «фотография» регистров на момент чтения.

### 3.1 Mvk01DeviceInfo

Читается `ReadDeviceInfoAsync`; источник — системная область `0x1000+`.

| Поле | Тип | Адрес | Смысл |
|---|---|---|---|
| `SerialNumber` | `string` | `0x1000`, 8 байт | Серийный номер устройства, прямой порядок, например `00004730` |
| `FirmwareVersion` | `Mvk01FirmwareVersion` | `0x1004` | Версия ПО, например `1.0.67.1` |
| `Errors` | `Mvk01SystemErrors` | `0x1006` | Битовый индикатор системных ошибок |
| `Uptime` | `TimeSpan` | `0x1008` | Наработка, с |
| `MemoryCardKilobytes` | `int` | `0x100A` | Размер карты памяти, КБ |
| `DspMd5` | `string` | `0x100C`, 16 байт | MD5 ПО DSP, hex в нижнем регистре без разделителей |
| `Unit1State` | `Mvk01UnitState` | `0x1014` | Состояние контроля по агрегату 1 |
| `Unit2State` | `Mvk01UnitState` | `0x1016` | Состояние контроля по агрегату 2 |
| `DspIndicators` | `Mvk01DspIndicators` | `0x1020`, 6 рег. | Индикаторы DSP: перегрузки каналов, флаги, счётчик расчётов |

Вложенные структуры (подробные таблицы полей — в
[главе 7a, раздел 8](07a-mvk01-register-map-and-codec.md#8-mvk01values--структуры-значений)):

| Структура | Поля |
|---|---|
| `Mvk01FirmwareVersion` | `Major`, `Minor`, `Patch`, `Build`; `ToString()` → `1.0.67.1` |
| `Mvk01SystemErrors` | `DspReceiveError`, `SdCardError`, `DspSendError`, `ModbusInitializationError`, `ConfigurationCrcError`, `Raw`, `IsClean` |
| `Mvk01UnitState` | `Zone` (`Mvk01Zone`), `SensorFailureEvent`, `EnteredYellowZone`, `EnteredRedZone`, `Raw` |
| `Mvk01DspIndicators` | `OverloadChannelMask`, `ConfigurationFlags`, `CalculationCounter`, метод `IsChannelOverloaded(channel)` |

Пример (числа согласованы с замером на стенде и тестовыми сценариями):

```csharp
Mvk01DeviceInfo info = await device.ReadDeviceInfoAsync();

Console.WriteLine(info.SerialNumber);       // 00004730
Console.WriteLine(info.FirmwareVersion);    // 1.0.67.1
Console.WriteLine(info.Uptime);             // 1.20:54:13 (161653 с)
Console.WriteLine(info.MemoryCardKilobytes); // 1024

if (!info.Errors.IsClean)
{
    Console.WriteLine($"DSP приём: {info.Errors.DspReceiveError}, SD-карта: {info.Errors.SdCardError}, "
        + $"DSP передача: {info.Errors.DspSendError}, Modbus: {info.Errors.ModbusInitializationError}, "
        + $"CRC конфигурации: {info.Errors.ConfigurationCrcError}");
}

if (info.Unit1State.Zone is Mvk01Zone.Yellow or Mvk01Zone.Red)
{
    Console.WriteLine("Агрегат 1 вышел из зелёной зоны");
}

bool overload1 = info.DspIndicators.IsChannelOverloaded(1);
uint calculations = info.DspIndicators.CalculationCounter;
```

Разбор версии: `1.0.67.1` — это `new Mvk01FirmwareVersion(1, 0, 67, 1)`, то есть
упакованное `0x01004301` (`Major<<24 | Minor<<16 | Patch<<8 | Build`). В тестах
эмулятор подставляет другую версию — `0x01071400` → `1.7.20.0`; сравнение
работает по значениям структуры, а не по строке.

Разбор ошибок: `Errors.Raw == 0` — ошибок нет (`IsClean == true`). В примере
выше ошибок не было; бит `SdCardError` покрывает сразу биты 1–3
(см. `DecodeSystemErrors`).

### 3.2 Mvk01Channel

Читается `ReadChannelAsync`; источник — блок расчётных параметров канала
(`ChannelData(channel)`, смещения от базы `A`).

| Поле | Тип | Смещение | Смысл |
|---|---|---|---|
| `Channel` | `int` | — | Номер канала (1–8) |
| `SensorVoltage` | `float` | `A+0x00` | Напряжение на датчике, В |
| `Spectrum` | `float[43]` | `A+0x02` | Дольоктавный спектр, полосы 1–43 (единицы по типу спектра устройства) |
| `PeakFactor` | `float` | `A+0x5C` | Пик-фактор виброускорения X…25 кГц |
| `RmsX25kHzAcceleration` | `float` | `A+0x5E` | СКЗ виброускорения X…25 кГц, м/с2 |
| `RmsSets` | `IReadOnlyList<Mvk01RmsSet>` | `A+0x60` | 8 наборов СКЗ (ускорение, скорость, перемещение) |
| `DcComponent` | `float` | `A+0x9A` | Постоянная составляющая, м/с2 |
| `State` | `Mvk01ChannelState` | `A+0x9E` | Индикаторы состояния канала (биты) |
| `EquivalentPeakValues` | `float[24]` | `A+0xA0` | Эквивалентные пиковые значения |
| `EquivalentRangeValues` | `float[24]` | `A+0xE0` | Эквивалентные значения размаха |
| `GetSpectrumBand(bandNumber)` | `float` (метод) | — | Значение полосы 1–43; вне диапазона — `ArgumentOutOfRangeException` |

Порядок наборов СКЗ (`RmsSets`), он же порядок параметров 49–72 в адресации
по номерам:

| Индекс | `Name` | Параметры (ускорение/скорость/перемещение) |
|---|---|---|
| 0 | `10…3000 Гц` | 49 / 50 / 51 |
| 1 | `10…2000 Гц` | 52 / 53 / 54 |
| 2 | `10…1000 Гц` | 55 / 56 / 57 |
| 3 | `2…1000 Гц` | 58 / 59 / 60 |
| 4 | `0.8…300 Гц` | 61 / 62 / 63 |
| 5 | `0.8…150 Гц` | 64 / 65 / 66 |
| 6 | `Фильтр 1` | 67 / 68 / 69 |
| 7 | `Фильтр 2` | 70 / 71 / 72 |

`Mvk01RmsSet` — запись с полями `Name`, `Acceleration` (м/с2), `Velocity` (мм/с),
`Displacement` (мкм).

`Mvk01ChannelState` — битовое поле (интерпретация `Mvk01Codec.DecodeChannelState`):

| Поле | Бит | Смысл |
|---|---|---|
| `Overload` | 0 | Перегрузка измерительного входа |
| `Buffer16HzNotFilled` | 4 | Буфер 16 Гц не заполнен |
| `Buffer64HzNotFilled` | 5 | Буфер 64 Гц не заполнен |
| `Buffer512HzNotFilled` | 6 | Буфер 512 Гц не заполнен |
| `DcVoltageAboveMaximum` | 10 | Постоянное напряжение выше нормы (вероятен обрыв кабеля) |
| `DcVoltageBelowMinimum` | 11 | Постоянное напряжение ниже нормы (вероятно КЗ) |
| `Raw` | — | Исходное `uint` |

Пример (значения согласованы с тестом `ReadChannelAsync_DecodesSnapshot`;
на стенде напряжение 7,82 В):

```csharp
Mvk01Channel channel = await device.ReadChannelAsync(1);

Console.WriteLine($"Напряжение: {channel.SensorVoltage:F2} В");            // 7,82 В
Console.WriteLine($"Пик-фактор: {channel.PeakFactor:F2}");                  // 3,50
Console.WriteLine($"СКЗ X…25 кГц: {channel.RmsX25kHzAcceleration:F3} м/с2"); // 12,250
Console.WriteLine($"DC-составляющая: {channel.DcComponent:F3} м/с2");       // 0,125

foreach (Mvk01RmsSet set in channel.RmsSets)
{
    Console.WriteLine($"{set.Name,-12} уск {set.Acceleration,8:F3}  "
        + $"скор {set.Velocity,8:F3}  перем {set.Displacement,8:F1}");
}
// 10…3000 Гц  уск    1,000  скор    2,000  перем      3,0
// ...
// Фильтр 2    уск   22,000  скор   23,000  перем     24,0

// Спектр: прямой массив или по номеру полосы
float band43 = channel.GetSpectrumBand(43);                  // полоса 43 = 10 кГц
double frequency = Mvk01SpectrumBands.GetCenterFrequency(43); // 10000 Гц
int topBand = Array.IndexOf(channel.Spectrum, channel.Spectrum.Max()) + 1;

if (channel.State.Overload)
{
    Console.WriteLine("Перегрузка входа канала");
}
```

Подписи полос удобно печатать через `Mvk01SpectrumBands.FormatBand(band)`
(«10 кГц», «0,63 Гц» — с учётом текущей культуры).

### 3.3 Mvk01TachoMeasurement

Читается `ReadTachoAsync`; источник — `TachoData(tacho)`: `0x3000` для тахометра 1,
`0x3100` для тахометра 2. Один запрос — 14 регистров.

| Поле | Тип | Смещение | Смысл |
|---|---|---|---|
| `Tacho` | `int` | — | Номер тахометра (1–2) |
| `FrequencyHz` | `float` | +0x00 | Частота вращения, Гц |
| `AverageIncrementHz` | `float` | +0x02 | Среднее приращение частоты на оборот, Гц |
| `SensorError` | `bool` | +0x04 | Ошибка датчика оборотов (период меньше минимально допустимого) |
| `TimestampSeconds` | `double` | +0x06 | Временная метка последнего перехода, с (регистр — 1/65536 с) |
| `InputLevel` | `bool` | +0x08 | Текущий уровень на входе тахометра |
| `Direction` | `Mvk01RotationDirection` | +0x0A | Направление вращения (только тахометр 1 при энкодере) |
| `TransitionCount` | `uint` | +0x0C | Число зарегистрированных переходов 0→1 |

`Mvk01RotationDirection`: `BelowMinimum` (0x00 — частота ниже минимальной),
`Tacho1ToTacho2` (0x02), `Tacho2ToTacho1` (0x03).

Пример (стенд: 17,14 Гц; тест эмулирует 17,13 Гц):

```csharp
Mvk01TachoMeasurement tacho = await device.ReadTachoAsync(1);

Console.WriteLine($"Частота: {tacho.FrequencyHz:F2} Гц");                 // 17,14
Console.WriteLine($"Приращение: {tacho.AverageIncrementHz:F3} Гц/оборот");
Console.WriteLine($"Направление: {tacho.Direction}");

if (tacho.SensorError)
{
    Console.WriteLine("Ошибка датчика оборотов — проверьте датчик и кабель");
}

if (!tacho.InputLevel && tacho.TransitionCount == 0)
{
    Console.WriteLine("Импульсов нет: вал стоит или датчик не подключён");
}
```

### 3.4 Mvk01ChannelConfig

Читается `ReadChannelConfigAsync`; источник — блок конфигурации канала
(`ChannelConfig(channel)`). Пять запросов — только нужные фрагменты блока.

| Поле | Тип | Смещение | Смысл |
|---|---|---|---|
| `Channel` | `int` | — | Номер канала (1–8) |
| `ChannelType` | `int` | +0x00 | Тип канала (по документации = 0) |
| `SensorFactor` | `float` | +0x02 | Коэффициент преобразования датчика, В/(м/с2) |
| `OverloadThreshold` | `float` | +0x04 | Порог перегрузки в долях |
| `Units` | `string` | +0x10, 32 байта | Единицы измерения (cp1251 + swap), например `м/с2` |
| `SerialNumber` | `string` | +0x20, 32 байта | Серийный номер датчика (cp1251 + swap), например `SN-42` |
| `FilterX25kHz` | `Mvk01FilterX25kHz` | +0x3E | Нижняя частота среза X…25 кГц: `Off`/`Hz6400`/`Hz10000`/`Hz15000` |
| `Filter1LowBand` | `int` | +0x40 | Фильтр 1 — номер нижней дольоктавы |
| `Filter1HighBand` | `int` | +0x42 | Фильтр 1 — номер верхней дольоктавы |
| `Filter2LowBand` | `int` | +0x44 | Фильтр 2 — номер нижней дольоктавы |
| `Filter2HighBand` | `int` | +0x46 | Фильтр 2 — номер верхней дольоктавы |
| `MaxDcVoltage` | `float` | +0x50 | Максимально допустимое постоянное напряжение, В |
| `MinDcVoltage` | `float` | +0x52 | Минимально допустимое постоянное напряжение, В |

Пример (значения согласованы с тестом `ReadChannelConfigAsync_DecodesSettings`):

```csharp
Mvk01ChannelConfig config = await device.ReadChannelConfigAsync(2);

Console.WriteLine($"Канал {config.Channel}: {config.Units}, датчик {config.SerialNumber}");
Console.WriteLine($"Коэффициент {config.SensorFactor:F3} В/(м/с2), порог {config.OverloadThreshold:F2}");
Console.WriteLine($"Фильтр X…25 кГц: {config.FilterX25kHz}");
Console.WriteLine($"Фильтр 1: полосы {config.Filter1LowBand}–{config.Filter1HighBand}");
Console.WriteLine($"Фильтр 2: полосы {config.Filter2LowBand}–{config.Filter2HighBand}");
Console.WriteLine($"DC-напряжение: от {config.MinDcVoltage:F1} до {config.MaxDcVoltage:F1} В");

if (config.Units.Length == 0)
{
    Console.WriteLine("Единицы не заданы — проверьте конфигурацию канала");
}
```

Тип канала `ChannelType` — информационное поле: на МВК01 ожидается `0`.
Резервный коэффициент пересчёта (`+0x0E`) в снимок не входит: его считает
устройство, писать туда нельзя (см. [главу 7a](07a-mvk01-register-map-and-codec.md)).

---

## 4. ReadChannelAsync подробно

`ReadChannelAsync(channel, ct)` — «тяжёлый», но самый информативный метод:
полный снимок канала одним вызовом. Внутри — **пять последовательных**
чтений holding-регистров (функция 0x03) через `IMvkDevice.ReadHoldingRegistersAsync`.

### 4.1 Какие запросы уходят

База канала `A = Mvk01RegisterMap.ChannelData(channel)`:
`0x2000 + (channel−1)×0x200`.

| № | Адрес | Регистров | Что попадает в снимок |
|---|---|---|---|
| 1 | `A + 0x00` | 96 (`0x60`) | `SensorVoltage` (`A+0x00`), `Spectrum` (`A+0x02`, 86 рег., полосы 1–43), `PeakFactor` (`A+0x5C`), `RmsX25kHzAcceleration` (`A+0x5E`) |
| 2 | `A + 0x60` | 48 | `RmsSets`: 8 наборов × 3 float (ускорение, скорость, перемещение) |
| 3 | `A + 0x9A` | 6 | `DcComponent` (`A+0x9A`), `State` (`A+0x9E`, uint32) |
| 4 | `A + 0xA0` | 48 | `EquivalentPeakValues` — 24 float |
| 5 | `A + 0xE0` | 48 | `EquivalentRangeValues` — 24 float |

Итого 246 регистров. Диапазон `A+0x90…A+0x99` (10 регистров) не читается —
это резерв между наборами СКЗ и постоянной составляющей.

### 4.2 Почему именно так

- **Лимит устройства — 125 регистров на запрос.** Весь блок канала (512 регистров)
  одним чтением получить нельзя физически.
- **Поля разбросаны по смещениям.** Спектр (86 рег.), наборы СКЗ (48 рег.),
  DC и состояние (6 рег.), эквивалентные значения (2 × 48 рег.) разделены
  промежутками; пять чтений покрывают всё нужное без «холостых» регистров.
- **Чётность адреса и количества.** Все пять запросов начинаются с чётных адресов
  и имеют чётное количество регистров — требование МВК01
  (`RequireEvenAddressAndQuantity`, см. [главу 7a, раздел 10](07a-mvk01-register-map-and-codec.md#10-особенности-проверенные-на-устройстве)).
- **Один фасадный вызов вместо пяти ручных.** `ReadChannelAsync` сам вызывает
  пять чтений и декодирует 32-битные значения с учётом `Settings.ByteOrder`.

### 4.3 Сколько это занимает

Пять запросов идут последовательно (запросы к одному устройству сериализуются,
`MaxConcurrentRequests = 1`). На реальном устройстве штатный снимок канала
укладывается в **единицы–десятки миллисекунд** (см. README, раздел «Периодический
опрос значений»). Худший случай при недоступном устройстве — пять таймаутов
по `ReadTimeoutMs` (по умолчанию 1000 мс) с учётом `MaxRetries`; поэтому цикл
опроса должен быть устойчив к `MvkModbusException` (см. раздел 9).

### 4.4 Когда лучше точечные альтернативы

| Нужно | Метод | Запросов |
|---|---|---|
| Полный снимок канала (спектр, все СКЗ, эквиваленты, состояние) | `ReadChannelAsync(channel)` | 5 |
| Один параметр сразу по всем 8 каналам | `ReadMeasurementByNumberAsync(number)` | 1 |
| Только состояние канала (биты) | `ReadMeasurementRawByNumberAsync(80)` | 1 |
| Сведения об устройстве | `ReadDeviceInfoAsync()` | 3 |
| Конфигурация канала | `ReadChannelConfigAsync(channel)` | 5 |

Снимок **не атомарный**: это пять разных запросов, между ними значения могут
обновиться DSP. Если для расчёта нужен согласованный набор — берите один
параметр по номеру (раздел 6) или помните о возможном «дрейфе» между полями.

---

## 5. Системные команды

### 5.1 Команды

`Mvk01SystemCommand` (запись в регистр `0x0002`, uint32, два регистра):

| Команда | Значение | Смысл |
|---|---|---|
| `RestoreConfiguration` | `0x10001` | Восстановить конфигурацию из Flash |
| `SaveConfiguration` | `0xF0001` | Сохранить текущую конфигурацию во Flash (таймаут ответа до 3 с) |
| `Reboot` | `0xA0001` | Перезапустить устройство |

### 5.2 Алгоритм ExecuteSystemCommandAsync

```csharp
public Task<Mvk01CommandResult> ExecuteSystemCommandAsync(
    Mvk01SystemCommand command,
    TimeSpan? timeout = null,
    CancellationToken cancellationToken = default)
```

По умолчанию `timeout` — **3 секунды** (`DefaultCommandTimeout`), как требует
документация для `SaveConfiguration`.

1. Кодирует команду в два регистра (`Mvk01Codec.EncodeSystemCommand` с учётом
   `Settings.ByteOrder`; для BigEndian `SaveConfiguration` — `[0x000F, 0x0001]`).
2. Выполняет `WriteMultipleRegisters` по адресу `0x0002` с общим таймаутом
   `effectiveTimeout`. Если запись не удалась — сразу возвращает результат
   `TransportError` с `Error` (исключение не бросается).
3. Запоминает дедлайн: `UtcNow + effectiveTimeout` (отсчёт начинается после записи).
4. В цикле читает два регистра `0x0002` (таймаут одного чтения —
   `min(effectiveTimeout, 1 с)`) и разбирает статус:
   - `0x00000` → `Success`;
   - `0x00BAD` → `BadCommand`;
   - `0xF0BAD` → `FlashError`;
   - любой другой статус → команда ещё выполняется, ожидание продолжается;
   - ошибка чтения **временная** (`IsTransient`, то есть `Transport`/`Timeout`/`Busy`) —
     цикл продолжается; **невременная** — возврат `TransportError` с `Error`.
5. Между итерациями — пауза **200 мс** (`CommandPollIntervalMs`), затем повтор.
6. Дедлайн истёк — результат `Timeout` (`DeviceStatus` не заполнен).

Отмена через `cancellationToken` проявляется как `OperationCanceledException`
(в том числе во время `Task.Delay` в цикле опроса).

### 5.3 Статусы

| `Mvk01CommandStatus` | Значение | Источник | Комментарий |
|---|---|---|---|
| `Success` | `0x00000` | устройство | Команда выполнена |
| `BadCommand` | `0x00BAD` | устройство | Устройство отклонило команду |
| `FlashError` | `0xF0BAD` | устройство | Ошибка Flash, конфигурация не сохранена |
| `Pending` | `−1` | — | **Объявлен, но текущей реализацией не возвращается**: неизвестный статус просто продолжает ожидание до таймаута |
| `Timeout` | `−2` | библиотека | Статус не получен за отведённое время |
| `TransportError` | `−3` | библиотека | Ошибка записи команды или чтения статуса; подробности — в `Error` |

Отрицательные значения — не биты регистра `0x0002`, а результат работы библиотеки;
не сравнивайте их с `DeviceStatus`.

### 5.4 Поля Mvk01CommandResult

| Поле | Тип | Смысл |
|---|---|---|
| `Command` | `Mvk01SystemCommand` | Отправленная команда |
| `Status` | `Mvk01CommandStatus` | Итоговый статус (см. таблицу выше) |
| `DeviceStatus` | `uint?` | Сырой статус из устройства, если он был прочитан; `null` при `Timeout`/ошибке записи |
| `Error` | `MvkModbusException?` | Ошибка транспорта, если была |
| `IsSuccess` | `bool` (вычисляемое) | `Status == Mvk01CommandStatus.Success` |
| `ToString()` | `string` | «Команда SaveConfiguration: Success» или с текстом ошибки |

### 5.5 Примеры всех трёх команд

```csharp
using System;
using Mvk.MVK01;

// Сохранение: таймаут по умолчанию 3 с (требование документации)
Mvk01CommandResult save = await device.SaveConfigurationAsync();
Console.WriteLine(save);                       // Команда SaveConfiguration: Success

// Восстановление из Flash
Mvk01CommandResult restore = await mvk.Device.RestoreConfigurationAsync();

// Перезапуск: устройство уйдёт на перезагрузку, связь восстановится автоматически
Mvk01CommandResult reboot = await device.RebootAsync();

// Явный таймаут — например, увеличенный для медленной сети
Mvk01CommandResult saveSlow = await device.ExecuteSystemCommandAsync(
    Mvk01SystemCommand.SaveConfiguration,
    TimeSpan.FromSeconds(10));

foreach (Mvk01CommandResult result in new[] { save, restore, reboot })
{
    Console.WriteLine(result.IsSuccess
        ? $"{result.Command}: выполнено"
        : $"{result.Command}: {result.Status}"
          + (result.Error is null ? string.Empty : $" ({result.Error.ErrorKind}: {result.Error.Message})"));
}
```

Почему для `Save` нужен таймаут 3 с: запись во Flash — самая медленная операция
устройства; документация прямо предписывает «таймаут ответа увеличить до 3 с».
Этот же дефолт используется для `Restore` и `Reboot`, поэтому отдельно задавать
таймаут не требуется.

### 5.6 Защита записи и разрешения

Регистр `0x0002` входит в защищённую область конфигурации `0x0000–0x0FFF`
(`WritePolicy = ProtectConfigArea`). При `AllowConfigurationWrite = false` middleware
`writeProtection` отклонит запись, и `ExecuteSystemCommandAsync` вернёт
`TransportError`, где `Error.ErrorKind == MvkErrorKind.Validation`:

```csharp
Mvk01CommandResult result = await device.SaveConfigurationAsync();

if (result.Status == Mvk01CommandStatus.TransportError && result.Error is not null)
{
    Console.WriteLine($"Не записано: {result.Error.ErrorKind} — {result.Error.Message}");
    // Например: Validation — Запись в защищённую область конфигурации ... запрещена
}
```

- `Mvk01Connection.ConnectAsync` по умолчанию оставляет `AllowConfigurationWrite = false`;
  для системных команд используйте `ConnectAsync(..., allowConfigurationWrite: true)`.
- `AddMvk01Devices()` не включает запись; задайте
  `Defaults.AllowConfigurationWrite = true` в конфигурации или коде.
- При ручной сборке пула/конфигурации разрешение нужно выставить явно.

На реальном стенде `Save`/`Reboot`/`Restore` выполнять только с явного разрешения.
Для проверки алгоритма используйте эмулятор `Mvk.Testing`:
в тестах `SaveConfigurationAsync_WritesCommandAndReadsSuccessStatus`,
`ExecuteSystemCommandAsync_BadCommandStatus_ReturnsBadCommand`,
`ExecuteSystemCommandAsync_FlashError_ReturnsFlashError`,
`ExecuteSystemCommandAsync_PendingStatus_TimesOut`,
`ExecuteSystemCommandAsync_WriteFails_ReturnsTransportError` покрывают все ветки.

---

## 6. Параметры по номерам

Раздел 5.1.2 карты регистров: все расчётные параметры доступны по номерам,
причём один параметр занимает 16 регистров — по 2 регистра на каждый из 8 каналов.
Это самый экономный способ читать значения по всем каналам сразу.

### 6.1 Методы

| Метод | Адрес | Возвращает |
|---|---|---|
| `ReadMeasurementByNumberAsync(Mvk01MeasurementNumber number, ct)` | `0x4000 + (N−1)×16` | `float[8]` — параметр по всем каналам |
| `ReadMeasurementByNumberAsync(int number, ct)` | То же | `float[8]` |
| `ReadMeasurementRawByNumberAsync(int number, ct)` | То же | `ushort[16]` — сырые регистры (битовые поля) |

Диапазон номера — `1..184` (`Mvk01RegisterMap.MaxMeasurementNumber`), иначе
`ArgumentOutOfRangeException`. Именованный enum — не полный: перечислены
основные параметры, остальные читаются числовым номером.

Пример адресов: N=1 → `0x4000`, N=48 → `0x42F0`, N=55 → `0x4360`, N=80 → `0x44F0`,
N=184 → `0x4B70`. Пример для N=48 в `MVK01_registers.txt` (`0x41F0`) ошибочен —
используйте формулу (см. [главу 7a](07a-mvk01-register-map-and-codec.md)).

### 6.2 Именованные номера Mvk01MeasurementNumber

| Номер | Имя | Смысл |
|---|---|---|
| 1 | `SensorVoltage` | Напряжение на датчике, В |
| 2 | `SpectrumBand1` | Полоса спектра 1 (0,63 Гц) |
| 44 | `SpectrumBand43` | Полоса спектра 43 (10 кГц) |
| 47 | `PeakFactorAcceleration` | Пик-фактор X…25 кГц |
| 48 | `RmsX25kHzAcceleration` | СКЗ виброускорения X…25 кГц, м/с2 |
| 49–51 | `Rms10To3000Acceleration/Velocity/Displacement` | 10…3000 Гц |
| 52–54 | `Rms10To2000...` | 10…2000 Гц |
| 55–57 | `Rms10To1000...` | 10…1000 Гц |
| 58–60 | `Rms2To1000...` | 2…1000 Гц |
| 61–63 | `Rms0_8To300...` | 0.8…300 Гц |
| 64–66 | `Rms0_8To150...` | 0.8…150 Гц |
| 67–69 | `RmsFilter1...` | Фильтр 1 |
| 70–72 | `RmsFilter2...` | Фильтр 2 |
| 78 | `DcComponent` | Постоянная составляющая, м/с2 |
| 79 | `SensorVoltageCopy` | Копия напряжения на датчике |
| 80 | `ChannelState` | Индикаторы состояния канала (uint32 на канал) |
| 81 | `EquivalentPeakStart` | Начало блока эквивалентных пиковых значений (24 параметра) |
| 123 | `EquivalentRangeStart` | Начало блока эквивалентных значений размаха (в первоисточнике диапазон указан как 123…136, значение 24) |
| 145 | `ChannelStateCopy` | Копия индикаторов состояния канала |
| 153 | `ControlParameter1Value` | Значение контролируемого параметра 1 (параметры 153–184) |

Числа 3–43 — полосы спектра 2–42, 45–46 и 73–77 — резерв/промежуточные номера
(см. [главу 7a, раздел 5](07a-mvk01-register-map-and-codec.md#5-mvk01measurementnumber--параметры-по-номерам)).

### 6.3 Пример: N=48 (СКЗ X…25 кГц) по всем каналам

```csharp
float[] rms = await device.ReadMeasurementByNumberAsync(
    Mvk01MeasurementNumber.RmsX25kHzAcceleration);   // адрес 0x42F0, один запрос

for (int i = 0; i < rms.Length; i++)
{
    Console.WriteLine($"Канал {i + 1}: {rms[i]:F4} м/с2");
}
// Канал 1: 12,2500 м/с2
// ...
// Канал 8: ...
```

### 6.4 Пример: N=55 (СКЗ 10…1000 Гц) по всем каналам

```csharp
float[] values = await device.ReadMeasurementByNumberAsync(
    Mvk01MeasurementNumber.Rms10To1000Acceleration);  // адрес 0x4360

Console.WriteLine(string.Join("  ", values.Select((v, i) => $"к{i + 1}={v:F4}")));
```

Именно этот вызов используется в режиме `watch` примера
`samples\Mvk.Sample.Console` и в шаблоне периодического опроса (раздел 8).

### 6.5 Пример: сырое чтение состояния канала (N=80)

Номер 80 — индикаторы состояния канала (по 2 регистра на канал).
`ReadMeasurementRawByNumberAsync(80)` возвращает `ushort[16]`; для канала `k`
его регистры — `raw[(k−1)×2]` и `raw[(k−1)×2 + 1]`.

```csharp
using Mvk.Modbus.Protocol;
using Mvk.MVK01;

ushort[] raw = await device.ReadMeasurementRawByNumberAsync(80);  // 0x44F0, 16 регистров

for (int channel = 1; channel <= Mvk01RegisterMap.MaxChannels; channel++)
{
    uint value = RegisterCodec.ToUInt32(
        raw.AsSpan((channel - 1) * 2, 2),
        device.Settings.ByteOrder);

    Mvk01ChannelState state = Mvk01Codec.DecodeChannelState(value);

    if (state.Overload)              Console.WriteLine($"к{channel}: перегрузка входа");
    if (state.DcVoltageAboveMaximum) Console.WriteLine($"к{channel}: DC выше нормы (обрыв?)");
    if (state.DcVoltageBelowMinimum) Console.WriteLine($"к{channel}: DC ниже нормы (КЗ?)");
    if (state.Buffer512HzNotFilled)  Console.WriteLine($"к{channel}: буфер 512 Гц не заполнен");
}
```

Разбор битов для `Raw = 0x00000401` (как в тесте `ReadChannelAsync_DecodesSnapshot`,
где состояние задано как `1 | (1 << 10)`):

| Бит | Значение | Поле |
|---|---|---|
| 0 | 1 | `Overload = true` |
| 4 | 0 | `Buffer16HzNotFilled = false` |
| 5 | 0 | `Buffer64HzNotFilled = false` |
| 6 | 0 | `Buffer512HzNotFilled = false` |
| 10 | 1 | `DcVoltageAboveMaximum = true` |
| 11 | 0 | `DcVoltageBelowMinimum = false` |

Эквивалентные значения (номера 81+ и 123+) при необходимости читаются так же:
`ReadMeasurementByNumberAsync(81 + i)` — пиковое значение эквивалента `i`,
`ReadMeasurementByNumberAsync(123 + i)` — значение размаха.

---

## 7. Фабрика фасадов и DI

### 7.1 IMvk01DeviceFactory

```csharp
public interface IMvk01DeviceFactory
{
    IMvk01Device GetDevice(string id);
    bool TryGetDevice(string id, out IMvk01Device device);
    IReadOnlyCollection<IMvk01Device> Devices { get; }
}
```

| Член | Поведение |
|---|---|
| `GetDevice(id)` | Возвращает фасад устройства пула; **кэширует** его. Неизвестный `id` — `MvkModbusException` с `MvkErrorKind.DeviceNotFound` |
| `TryGetDevice(id, out device)` | Безопасный вариант: `false`, если устройства нет в пуле; при `true` возвращает (и кэширует) фасад |
| `Devices` | Все устройства пула в типизированном виде (`IReadOnlyCollection<IMvk01Device>`) |

Реализация `Mvk01DeviceFactory`:

- хранит кэш `ConcurrentDictionary<string, Mvk01Device>` с компаратором
  `OrdinalIgnoreCase` — повторные вызовы `GetDevice` для одного `Id` не создают
  новых объектов и не плодят обёрток;
- конструктор `Mvk01DeviceFactory(IMvkDevicePool pool, Encoding? textEncoding = null)`,
  где `textEncoding` по умолчанию — `Mvk01Codec.DefaultTextEncoding` (**cp1251**);
- `Devices` — проекция `pool.Devices` через `GetDevice`, то есть один и тот же
  фасад на устройство.

### 7.2 AddMvk01Devices

```csharp
public static IServiceCollection AddMvk01Devices(this IServiceCollection services)
```

Что делает:

1. `services.AddOptions<MvkModbusOptions>().PostConfigure(...)` — после всех
   `Configure`-вызовов принудительно выставляет
   `Defaults.RequireEvenAddressAndQuantity = true` (чётность адреса и количества
   регистров — требование МВК01). Запись в защищённую область не включается:
   для команд Save/Restore/Reboot задайте `Defaults.AllowConfigurationWrite = true`
   явно.
2. `TryAddSingleton<IMvk01DeviceFactory>(...)` — регистрирует
   `new Mvk01DeviceFactory(provider.GetRequiredService<IMvkDevicePool>())`.
   `TryAdd` означает: если `IMvk01DeviceFactory` уже зарегистрирована (например,
   вашей фабрикой с нужной кодировкой), регистрация не перезаписывается.

Вызывать **после** `AddMvkModbus(...)`; hosted-сервис и middleware — как обычно:

```csharp
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "mvkmodbus.json"),
    optional: false,
    reloadOnChange: false);

builder.Services.AddMvkModbus(builder.Configuration);
builder.Services.AddMvk01Devices();
builder.Services.AddMvkModbusHostedService();

using IHost host = builder.Build();
await host.StartAsync();

IMvk01DeviceFactory factory = host.Services.GetRequiredService<IMvk01DeviceFactory>();

foreach (IMvk01Device device in factory.Devices)
{
    Mvk01DeviceInfo info = await device.ReadDeviceInfoAsync();
    Console.WriteLine($"{device.Id} ({device.Settings.Endpoint}): "
        + $"ПО {info.FirmwareVersion}, S/N {info.SerialNumber}, состояние {device.State}");
}

IMvk01Device? mvk = factory.TryGetDevice("mvk-1", out IMvk01Device found) ? found : null;
if (mvk is not null)
{
    Mvk01Channel channel = await mvk.ReadChannelAsync(1);
    Console.WriteLine($"Канал 1: {channel.SensorVoltage:F2} В");
}

await host.StopAsync();
```

Если приложению нужны `Save/Restore/Reboot`, разрешите запись явно:
`options.Defaults.AllowConfigurationWrite = true` в `AddMvkModbus(...)` или
`"Defaults": { "AllowConfigurationWrite": true }` в JSON.

Если нужна другая кодировка строк, зарегистрируйте фабрику сами
**до** `AddMvk01Devices()`:

```csharp
builder.Services.AddSingleton<IMvk01DeviceFactory>(provider =>
    new Mvk01DeviceFactory(
        provider.GetRequiredService<IMvkDevicePool>(),
        Mvk01Codec.DefaultTextEncoding));   // cp1251 для Units/SerialNumber
builder.Services.AddMvk01Devices();         // TryAddSingleton не перезапишет регистрацию
```

---

## 8. Производительность и паттерны

### 8.1 «Тяжёлый» и «лёгкий» методы

| Метод | Запросов | Регистров | Когда использовать |
|---|---|---|---|
| `ReadChannelAsync(channel)` | 5 | 246 | Полный снимок канала: спектр, все СКЗ, эквиваленты, состояние. Редко (по требованию, раз в секунды) |
| `ReadChannelConfigAsync(channel)` | 5 | 52 | Конфигурация канала — при настройке/диагностике |
| `ReadDeviceInfoAsync()` | 3 | 30 | Сведения об устройстве — при старте/диагностике |
| `ReadTachoAsync(tacho)` | 1 | 14 | Снимок тахометра |
| `ReadMeasurementByNumberAsync(number)` | 1 | 16 | **Основной метод периодического опроса**: один параметр по всем 8 каналам |
| `ReadMeasurementRawByNumberAsync(number)` | 1 | 16 | Битовые поля (состояние и т.п.) |

Для периодического опроса выбирайте `ReadMeasurementByNumberAsync`:
один запрос вместо пяти, значения по всем каналам канала сразу, и они
согласованы между каналами (один параметр — один запрос).

### 8.2 Периодический опрос: готовый паттерн

Ручной вариант с `PeriodicTimer`; в большинстве приложений проще
`Mvk01Watcher` из раздела 8.4.

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Mvk.Modbus;
using Mvk.MVK01;

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
        // Транспортные ошибки: реконнект уже идёт в фоне, опрос продолжаем.
        Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  ошибка: {ex.ErrorKind} — {ex.Message}");
    }
}
```

`PeriodicTimer` не накапливает дрейф, в отличие от `Task.Delay`. Запросы
к одному устройству сериализуются (`MaxConcurrentRequests = 1`), поэтому
искусственные паузы между вызовами не нужны.

### 8.3 Чего избегать

- **Не создавайте `Mvk01Device` вручную на каждый вызов.** Фасад лёгкий, но
  в DI-сценарии есть кэширующая `IMvk01DeviceFactory`; в простом — один
  `Mvk01Connection` на всё приложение.
- **Не запускайте параллельные циклы опроса одного устройства.** Они встанут
  в очередь и только увеличат задержки; лучше один цикл, читающий все нужные
  параметры подряд.
- **Не читайте `ReadChannelAsync` с периодом меньше секунды**, если нужен
  только один параметр: 5 запросов × 246 регистров дают лишний трафик.
- **Не полагайтесь на атомарность снимка канала** — это 5 запросов. Для
  согласованного набора используйте параметр по номеру.
- **Не дублируйте чтение.** Один `ReadMeasurementByNumberAsync(55)` уже вернул
  значения по всем каналам — не вызывайте его в цикле по каналам.

### 8.4 `Mvk01Watcher` — наблюдение без boilerplate

`Mvk01Watcher` инкапсулирует периодический опрос: интервал, отмену,
обработку ошибок и доступ к значениям каналов. Это надстройка над
`ReadMeasurementByNumberAsync`, низкоуровневый API остаётся доступен.

```csharp
using Mvk.MVK01.Polling;

Mvk01Watcher watcher = new(device, TimeSpan.FromMilliseconds(500));
Mvk01PollItem item = new(
    Mvk01MeasurementNumber.Rms10To1000Acceleration,
    channel: 1);

await foreach (Mvk01Sample sample in watcher.WatchAsync(item, cts.Token))
{
    if (sample.Error is { } error)
    {
        // Ошибка связи: наблюдение продолжается, реконнект идёт автоматически.
        Console.WriteLine($"{sample.Timestamp.ToLocalTime():HH:mm:ss.fff}  ошибка: {error.ErrorKind}");
        continue;
    }

    Console.WriteLine($"{sample.Timestamp.ToLocalTime():HH:mm:ss.fff}  {sample.ChannelValue:F4} м/с2");
}
```

Несколько параметров за один цикл передаются массивом `Mvk01PollItem[]`;
полный пример — в рецепте 15.

Поведение:

| Свойство | Значение |
|---|---|
| Первый опрос | Сразу, не через интервал |
| Интервал | От начала цикла; при перегрузке следующий цикл начинается сразу, без наложения запросов |
| `Mvk01Sample.Values` | Всегда 8 значений — параметр читается одним запросом по всем каналам |
| `Mvk01Sample.ChannelValue` | Значение выбранного `Item.Channel` либо `null` при ошибке |
| Ошибки | Приходят в `Mvk01Sample.Error`; поток продолжается |
| Отмена | `OperationCanceledException` наружу, как у обычного `IAsyncEnumerable` |
| Поток | Один активный `await foreach` на устройство; для нескольких устройств — отдельные watcher'ы |

`Mvk01Watcher` не создаёт соединение сам: первый запрос лениво подключается
через `IMvk01Device`, а реконнект выполняет пул. Watcher опрашивает параметры
по номерам (`ReadMeasurementByNumberAsync`); для полного снимка канала со
спектром и состоянием используйте `ReadChannelAsync` в собственном цикле —
это другой профиль нагрузки (5 запросов вместо одного).

Полные примеры фоновой службы, опроса нескольких параметров и слияния потоков
от нескольких устройств — в [рецептах 15 и 16](11-recipes.md).

---

## 9. Обработка ошибок

### 9.1 Кто что бросает

| Источник | Исключение | Когда |
|---|---|---|
| Все методы чтения фасада | `MvkModbusException` | Транспорт, таймаут, ошибка устройства, `DeviceDisabled` и т.д. |
| `ReadChannelAsync`/`ReadTachoAsync`/`ReadMeasurement*` | `ArgumentOutOfRangeException` | Канал вне 1–8, тахометр вне 1–2, номер вне 1–184 |
| `Mvk01Device(device, ...)` | `ArgumentNullException` | `device == null` |
| `Mvk01Connection.ConnectAsync("")` | `ArgumentException` | Пустой адрес |
| `ExecuteSystemCommandAsync` и обёртки | **не бросают** транспортных исключений | Результат в `Mvk01CommandResult` (`Status`, `Error`) |
| Любой метод при отмене | `OperationCanceledException` | Отменён `CancellationToken` |

`MvkModbusException` содержит `ErrorKind` (`Transport`, `Timeout`, `Busy`,
`DeviceError`, `DeviceNotFound`, `DeviceDisabled`, `Validation`, `Configuration`,
`Canceled`, `Unknown`), `DeviceId`, `ModbusExceptionCode` и вычисляемое
`IsTransient` (истинно для `Transport`, `Timeout`, `Busy`).

### 9.2 Типовой try/catch

```csharp
using Mvk.Modbus;
using Mvk.MVK01;

try
{
    Mvk01Channel channel = await device.ReadChannelAsync(1, ct);
    Console.WriteLine($"Напряжение: {channel.SensorVoltage:F2} В");
}
catch (MvkModbusException ex) when (ex.IsTransient)
{
    // Связь: реконнект уже выполняется автоматически. Выдержите паузу и повторите.
    Console.WriteLine($"МВК01 недоступен: {ex.ErrorKind} — {ex.Message}");
}
catch (MvkModbusException ex)
{
    // Данные/конфигурация: устройство отключено, политика записи, ошибка устройства.
    Console.WriteLine($"Ошибка: {ex.ErrorKind} — {ex.Message} (устройство {ex.DeviceId})");
}
```

### 9.3 Что делать при частых ошибках

| Ошибка | `IsTransient` | Действия |
|---|---|---|
| `Timeout` | да | Проверить сеть/питание; увеличить `ReadTimeoutMs`; выдержать паузу — реконнект в фоне; не забивать устройство параллельными циклами |
| `Transport` | да | То же; проверить кабель и настройки IP |
| `DeviceDisabled` | нет | Устройство выключено в конфигурации (`Enabled = false`) — включите или не опрашивайте; реконнект не поможет |
| `DeviceNotFound` | нет | Неверный `Id` — сверьтесь с конфигурацией или `TryGetDevice` |
| `Validation` | нет | Нарушена политика записи (например, `AllowConfigurationWrite = false`) или чётность адреса/количества |
| `DeviceError` | нет | Устройство вернуло код ошибки Modbus (например, 0x01 на нечётный запрос) — исправьте запрос/конфигурацию |

Для системных команд помните: `TransportError` в результате — это **не** исключение,
а признак того, что команда не подтверждена. Смотрите `result.Error?.ErrorKind`
и `result.DeviceStatus`:

```csharp
Mvk01CommandResult result = await device.SaveConfigurationAsync();

switch (result.Status)
{
    case Mvk01CommandStatus.Success:
        Console.WriteLine("Сохранено во Flash");
        break;
    case Mvk01CommandStatus.FlashError:
        Console.WriteLine("Устройство сообщило об ошибке Flash — конфигурация не сохранена");
        break;
    case Mvk01CommandStatus.BadCommand:
        Console.WriteLine("Устройство отклонило команду");
        break;
    case Mvk01CommandStatus.Timeout:
        Console.WriteLine("Статус не получен за 3 с — проверьте связь и повторите");
        break;
    case Mvk01CommandStatus.TransportError:
        Console.WriteLine($"Ошибка транспорта: {result.Error?.ErrorKind}");
        break;
}
```

---

## 10. Примеры

Во всех примерах ниже предполагается, что `IMvk01Device device` получен
из `Mvk01Connection` или `IMvk01DeviceFactory`; `ct` — токен отмены.

### 10.1 Разовый обзор устройства

```csharp
using System;
using Mvk.MVK01;

await using Mvk01Connection mvk = await Mvk01Connection.ConnectAsync("10.20.0.211");
IMvk01Device device = mvk.Device;

Mvk01DeviceInfo info = await device.ReadDeviceInfoAsync();
Console.WriteLine($"МВК01 {info.SerialNumber}, ПО {info.FirmwareVersion}, наработка {info.Uptime}");
Console.WriteLine($"Карта памяти: {info.MemoryCardKilobytes} КБ; ошибки: 0x{info.Errors.Raw:X8}");
Console.WriteLine($"Агрегат 1: {info.Unit1State.Zone}, агрегат 2: {info.Unit2State.Zone}");

Mvk01Channel channel = await device.ReadChannelAsync(1);
Console.WriteLine($"Канал 1: {channel.SensorVoltage:F2} В, "
    + $"СКЗ X…25 кГц {channel.RmsX25kHzAcceleration:F3} м/с2");
Console.WriteLine($"10…1000 Гц: {channel.RmsSets[2].Acceleration:F3} м/с2");

Mvk01TachoMeasurement tacho = await device.ReadTachoAsync(1);
Console.WriteLine($"Тахометр 1: {tacho.FrequencyHz:F2} Гц, {tacho.Direction}");
```

### 10.2 Периодический опрос одного параметра

```csharp
using System;
using System.Threading;
using Mvk.Modbus;
using Mvk.MVK01;

using CancellationTokenSource cts = new();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(500));

while (await timer.WaitForNextTickAsync(cts.Token))
{
    try
    {
        float[] rms = await device.ReadMeasurementByNumberAsync(
            Mvk01MeasurementNumber.Rms10To1000Acceleration, cts.Token);

        string line = string.Join("  ", rms.Select((v, i) => $"к{i + 1}={v:F3}"));
        Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  {line}");
    }
    catch (MvkModbusException ex)
    {
        Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  {ex.ErrorKind}: {ex.Message}");
    }
}
```

### 10.3 Чтение всех 8 каналов и вывод таблицы

```csharp
using Mvk.MVK01;

float[] acceleration = await device.ReadMeasurementByNumberAsync(
    Mvk01MeasurementNumber.Rms10To1000Acceleration);
float[] velocity = await device.ReadMeasurementByNumberAsync(
    Mvk01MeasurementNumber.Rms10To1000Velocity);

Console.WriteLine($"{"Канал",-6}{"Ускорение, м/с2",18}{"Скорость, мм/с",16}");
Console.WriteLine(new string('-', 40));

for (int i = 0; i < acceleration.Length; i++)
{
    Console.WriteLine($"{i + 1,-6}{acceleration[i],18:F3}{velocity[i],16:F3}");
}

// Канал  Ускорение, м/с2  Скорость, мм/с
// ----------------------------------------
// 1              5,431           1,204
// 2              5,398           1,198
// ...
```

Для полного снимка одного канала (спектр + эквиваленты) используйте
`ReadChannelAsync`:

```csharp
Mvk01Channel channel = await device.ReadChannelAsync(1);

float maxBandValue = channel.Spectrum.Max();
int maxBand = Array.IndexOf(channel.Spectrum, maxBandValue) + 1;
Console.WriteLine($"Максимум спектра: {Mvk01SpectrumBands.FormatBand(maxBand)} — {maxBandValue:F4}");
Console.WriteLine($"Эквивалентных пиков: {channel.EquivalentPeakValues.Length}, размахов: {channel.EquivalentRangeValues.Length}");
```

### 10.4 Чтение конфигурации канала

```csharp
using Mvk.MVK01;

Mvk01ChannelConfig config = await device.ReadChannelConfigAsync(1);

Console.WriteLine($"Канал {config.Channel}: тип {config.ChannelType}, "
    + $"единицы «{config.Units}», датчик {config.SerialNumber}");
Console.WriteLine($"Коэффициент: {config.SensorFactor:F3} В/(м/с2), "
    + $"порог перегрузки: {config.OverloadThreshold:F2}");
Console.WriteLine($"Фильтр X…25 кГц: {config.FilterX25kHz}");
Console.WriteLine($"Полосы фильтра 1: {config.Filter1LowBand}–{config.Filter1HighBand}, "
    + $"фильтра 2: {config.Filter2LowBand}–{config.Filter2HighBand}");
Console.WriteLine($"DC-напряжение: {config.MinDcVoltage:F1}…{config.MaxDcVoltage:F1} В");

// Проверка, что строка декодирована корректно (cp1251 + swap)
if (config.Units.Contains('м') || config.Units == "m/s2")
{
    Console.WriteLine("Единицы прочитаны корректно");
}
```

### 10.5 Сохранение конфигурации с проверкой результата

```csharp
using System;
using Mvk.MVK01;

// ВНИМАНИЕ: на реальном устройстве Save выполнять только с явного разрешения.
Mvk01CommandResult result = await device.SaveConfigurationAsync(ct);

if (result.IsSuccess)
{
    Console.WriteLine($"Конфигурация сохранена во Flash (статус 0x{result.DeviceStatus:X5})");
    Console.WriteLine($"Команда: {result.Command}, результат: {result}");
}
else if (result.Status == Mvk01CommandStatus.FlashError)
{
    Console.WriteLine("Ошибка Flash: конфигурация НЕ сохранена, повторите попытку");
}
else if (result.Status == Mvk01CommandStatus.TransportError)
{
    Console.WriteLine($"Команда не дошла: {result.Error?.ErrorKind} — {result.Error?.Message}");
}
else
{
    Console.WriteLine($"Не подтверждено: {result.Status}, статус устройства 0x{result.DeviceStatus:X5}");
}
```

Альтернативный вариант — с увеличенным таймаутом для медленной сети:

```csharp
Mvk01CommandResult result = await device.ExecuteSystemCommandAsync(
    Mvk01SystemCommand.SaveConfiguration,
    TimeSpan.FromSeconds(10),
    ct);
```

---

## Подводные камни

- **`ConnectAsync` бросает исключение сразу.** Это не ленивое подключение:
  недоступное устройство — `MvkModbusException` с `Timeout`/`Transport`.
  Если нужен цикл ожидания — используйте пул с `ReconnectEnabled`.
- **Идентификатор в `Mvk01Connection` — адрес хоста.** Два устройства за одним
  IP (разные порты/`SlaveId`) так не подключить: `Id = host.Trim()`.
- **У `Mvk01Connection` нет чтения по номерам и команд Restore/Reboot.**
  Они есть у `IMvk01Device` — обращайтесь через `mvk.Device`.
- **Снимок канала не атомарный.** `ReadChannelAsync` — пять последовательных
  запросов; значения между ними могут обновиться. Для согласованного набора —
  `ReadMeasurementByNumberAsync`.
- **Стоимость снимка.** 5 запросов × до `ReadTimeoutMs` (по умолчанию 1000 мс)
  в худшем случае при недоступном устройстве; не вызывайте `ReadChannelAsync`
  в тесном цикле без обработки ошибок.
- **Промежуток `A+0x90…A+0x99` не читается.** Это резерв; в снимке его нет
  по замыслу, а не по ошибке.
- **`DeviceStatus` бывает `null`.** При `Timeout` и ошибке записи статус
  от устройства не получен — не форматируйте его как число без проверки.
- **Неизвестный статус команды ждёт таймаут.** `Pending` объявлен в перечислении,
  но текущий алгоритм не возвращает его: любой незнакомый статус в регистре
  приводит к ожиданию до истечения таймаута и результату `Timeout`.
- **Отказ политики записи выглядит как `TransportError`.** Если
  `AllowConfigurationWrite = false`, команда вернёт `TransportError`, а
  `Error.ErrorKind` будет `Validation`; проверяйте `Error`, а не только `Status`.
- **Три секунды для Save не случайны.** Документация МВК01 требует таймаут
  до 3 с на сохранение во Flash; этот же дефолт используется для Restore/Reboot.
  Уменьшение таймаута даст ложный `Timeout` (см. тест
  `ExecuteSystemCommandAsync_PendingStatus_TimesOut` с 300 мс).
- **`ArgumentOutOfRangeException` — не ошибка связи.** Канал 0/9, тахометр 0/3,
  номер 0/185 — ошибка программиста; `MvkModbusException` здесь не бросается.
- **Кодировка строк — cp1251 по умолчанию** (`Mvk01Codec.DefaultTextEncoding`)
  и у `Mvk01Device`, и у `Mvk01DeviceFactory`. Если провайдер кодовых страниц
  недоступен, происходит откат в Latin1, и «м/с2» декодируется как «ì/ñ2».
- **Кэш фабрики не синхронизирован с `RemoveDevice`.** После удаления устройства
  из пула фасад с тем же `Id` может остаться в кэше фабрики и ссылаться
  на старое устройство; при динамическом составе пула создавайте новый фасад
  или пересоздавайте фабрику.
- **`RmsSets[0]` — это 10…3000 Гц, а не X…25 кГц.** Отдельное значение
  X…25 кГц — `RmsX25kHzAcceleration` (параметр 48).
- **Эквивалентные значения нумеруются не подряд с СКЗ.** В адресации по номерам
  их блоки начинаются с 81 (пики) и 123 (размахи), а не с 49; пометка «пар. 49–72»
  в первоисточнике означает соответствие наборам СКЗ, а не номера параметров.
- **cp1251 требует провайдера кодовых страниц.** Пакет
  `System.Text.Encoding.CodePages` — зависимость `Mvk.MVK01`; без него
  `DefaultTextEncoding` откатывается в Latin1, и «м/с2» превращается в «ì/ñ2».
- **`Mvk01SpectrumBands.FormatBand` использует текущую культуру** («0,63 Гц»
  против «0.63 Гц») — важно при экспорте и парсинге подписей.
- **Консоль и «м/с2».** Если в Windows-консоли «кракозябры», задайте
  `Console.OutputEncoding = System.Text.Encoding.UTF8;`.
- **Освобождайте ресурсы.** `Mvk01Connection` и `MvkDevicePool` — `IAsyncDisposable`;
  без `await using`/`DisposeAsync` фоновый цикл и сокеты могут помешать
  завершению приложения.
- **Отмена — это `OperationCanceledException`**, а не `MvkModbusException`;
  в цикле опроса её обрабатывают отдельно (через `WaitForNextTickAsync` или
  `catch (OperationCanceledException)`).
- **Save/Reboot/Restore на реальном железе** — только с явного разрешения:
  команды меняют энергонезависимую конфигурацию и перезапускают устройство.

---

## См. также

- [Карта регистров и кодеки](07a-mvk01-register-map-and-codec.md) — адреса, формулы,
  `Mvk01RegisterMap`, `Mvk01Codec`, `Mvk01MeasurementNumber`, структуры значений.
- [Пул устройств](05-device-pool.md) — `IMvkDevicePool`, `IMvkDevice`, состояния,
  реконнект, события, сырые регистры.
- [Рецепты](11-recipes.md) — прикладные сценарии: периодический опрос, WPF-привязки,
  хранение истории измерений.
- [Уставки и тревоги](15-setpoints.md) — прикладные пороги, гистерезис и реакции.
- [README](../README.md) — быстрый старт, конфигурация, справочник API и частые вопросы.
