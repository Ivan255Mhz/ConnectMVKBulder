# Глава 2. Первое подключение

Цель главы — от нуля дойти до работающего приложения: подключиться к МВК01,
прочитать декодированные значения, затем перейти к нескольким устройствам из
JSON-конфигурации и, наконец, к интеграции с DI. Все примеры рассчитаны на
реальное устройство `10.20.0.211:502` (SlaveId 1, ПО 1.0.67.1), но выполняют
**только чтение**.

## 2.1. Подготовка

### 2.1.1. Ссылки на проекты

Библиотека распространяется в виде исходных проектов, поэтому в своё приложение
нужно добавить ссылки на `Mvk.Modbus` (транспорт, пул, конфигурация) и
`Mvk.MVK01` (типизированный слой). Минимальный `.csproj` консольного приложения:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Mvk.Modbus\Mvk.Modbus.csproj" />
    <ProjectReference Include="..\..\src\Mvk.MVK01\Mvk.MVK01.csproj" />
  </ItemGroup>

</Project>
```

Относительные пути зависят от расположения вашего проекта; в репозитории
библиотеки проекты лежат в `src\Mvk.Modbus` и `src\Mvk.MVK01`. Если приложение
создаётся внутри решения `ConnectMVKBulder.sln`, дополнительно ничего настраивать
не нужно: общие свойства (`net8.0`, `Nullable enable`, `ImplicitUsings enable`)
заданы в `Directory.Build.props`.

Проект `Mvk.MVK01` уже ссылается на `Mvk.Modbus`, но явная ссылка на оба
проекта делает зависимости наглядными и повторяет пример
`samples\Mvk.Sample.Console`.

### 2.1.2. Проверка доступности устройства

Перед первым запуском убедитесь, что устройство видно в сети:

```powershell
Test-NetConnection 10.20.0.211 -Port 502
```

Ожидаемый результат — `TcpTestSucceeded : True`. Если `False`, проверьте:

- IP-адрес и порт (по умолчанию `502`);
- что компьютер находится в той же подсети и порт не блокируется брандмауэром;
- что Modbus/TCP-порт устройства не изменён (регистр `0x000A` карты МВК01).

## 2.2. Уровень 0. Одно устройство: `Mvk01Connection`

`Mvk01Connection` — самая простая точка входа. Конфигурация не нужна: адрес
задаётся строкой, остальные параметры (порт 502, SlaveId 1, BigEndian, таймауты,
реконнект) берутся из значений по умолчанию.

### 2.2.1. Полный пример

Создайте консольное приложение и замените `Program.cs`:

```csharp
using Mvk.MVK01;

Console.OutputEncoding = System.Text.Encoding.UTF8;

// Подключение: порт 502, SlaveId 1, BigEndian — по умолчанию.
await using Mvk01Connection mvk = await Mvk01Connection.ConnectAsync("10.20.0.211");
Console.WriteLine("Соединение установлено.");

// 1. Общие сведения об устройстве.
Mvk01DeviceInfo info = await mvk.ReadDeviceInfoAsync();
Console.WriteLine($"Серийный номер:    {info.SerialNumber}");
Console.WriteLine($"Версия ПО:         {info.FirmwareVersion}");
Console.WriteLine($"Наработка:         {info.Uptime}");
Console.WriteLine($"Системные ошибки:  0x{info.Errors.Raw:X8} (IsClean = {info.Errors.IsClean})");
Console.WriteLine($"Агрегат 1:         зона {info.Unit1State.Zone}, карта памяти {info.MemoryCardKilobytes} КБ");

// 2. Полный снимок канала 1.
Mvk01Channel channel = await mvk.ReadChannelAsync(1);
Console.WriteLine();
Console.WriteLine($"Канал {channel.Channel}: напряжение {channel.SensorVoltage:F4} В");
Console.WriteLine($"  СКЗ X…25 кГц:      {channel.RmsX25kHzAcceleration:F3} м/с2");
Console.WriteLine($"  Пик-фактор:        {channel.PeakFactor:F3}");
Console.WriteLine($"  Постоянная сост.:  {channel.DcComponent:F4} м/с2");
Console.WriteLine($"  Полос спектра:     {channel.Spectrum.Length}");
Console.WriteLine($"  Состояние:         перегрузка = {channel.State.Overload}");

// 3. Наборы СКЗ по полосам частот.
Console.WriteLine("  СКЗ по полосам:");
foreach (Mvk01RmsSet set in channel.RmsSets)
{
    Console.WriteLine($"    {set.Name,-12} уск {set.Acceleration,8:F4}  "
        + $"скорость {set.Velocity,8:F4}  перемещение {set.Displacement,8:F4}");
}

// 4. Топ-5 полос дольоктавного спектра.
Console.WriteLine("  Топ-5 полос спектра:");
foreach ((int band, float value) in channel.Spectrum
    .Select((value, index) => (Band: index + 1, Value: value))
    .OrderByDescending(item => item.Value)
    .Take(5))
{
    Console.WriteLine($"    {Mvk01SpectrumBands.FormatBand(band),-10} {value,10:F4}");
}

// 5. Снимок канала тахометра.
Mvk01TachoMeasurement tacho = await mvk.ReadTachoAsync(1);
Console.WriteLine();
Console.WriteLine($"Тахометр 1: {tacho.FrequencyHz:F3} Гц, направление {tacho.Direction}, "
    + $"переходов {tacho.TransitionCount}, ошибка датчика = {tacho.SensorError}");
```

Запуск:

```powershell
dotnet run
```

### 2.2.2. Шаг 1. Подключение

```csharp
await using Mvk01Connection mvk = await Mvk01Connection.ConnectAsync("10.20.0.211");
```

Сигнатура:

```csharp
public static Task<Mvk01Connection> ConnectAsync(
    string host,
    int port = 502,
    byte slaveId = 1,
    CancellationToken cancellationToken = default)
```

- `port` и `slaveId` можно не указывать — примут значения 502 и 1;
- метод **сразу проверяет связь**: если устройство недоступно, выбрасывается
  `MvkModbusException` с `ErrorKind` = `Timeout` (не успели подключиться за
  `ConnectTimeoutMs` = 3000 мс) или `Transport` (сокет отверг соединение);
- пустой `host` — `ArgumentException`;
- `Mvk01Connection` реализует `IAsyncDisposable`: `await using` закрывает
  соединение и освобождает пул устройств.

`Mvk01Connection` — это обёртка над пулом, состоящим из одного устройства:
дополнительно включаются правила МВК01 (`RequireEvenAddressAndQuantity = true`)
и разрешается запись конфигурации (`AllowConfigurationWrite = true`, нужно для
системных команд). Идентификатор устройства внутри пула равен строке `host`.

### 2.2.3. Шаг 2. Сведения об устройстве

```csharp
Mvk01DeviceInfo info = await mvk.ReadDeviceInfoAsync();
```

Возвращает запись `Mvk01DeviceInfo`:

| Поле | Тип | Смысл |
|---|---|---|
| `SerialNumber` | `string` | серийный номер устройства (8 байт, прямой порядок) |
| `FirmwareVersion` | `Mvk01FirmwareVersion` | `Major.Minor.Patch.Build`, `ToString()` → `"1.0.67.1"` |
| `Errors` | `Mvk01SystemErrors` | биты ошибок DSP/SD-карты/Modbus/CRC; `Raw`, `IsClean` |
| `Uptime` | `TimeSpan` | наработка с момента включения |
| `MemoryCardKilobytes` | `int` | размер установленной карты памяти, КБ |
| `DspMd5` | `string` | MD5 внутреннего ПО DSP (нижний регистр, без разделителей) |
| `Unit1State`, `Unit2State` | `Mvk01UnitState` | зона контроля (`Mvk01Zone`) и события входа в жёлтую/красную зону |
| `DspIndicators` | `Mvk01DspIndicators` | маска перегрузок каналов, флаги конфигурации, счётчик расчётов |

Расшифровка версии: значение регистра `0x1004` — упакованный `uint32`
(байты: major, minor, patch, build). Например, `0x01071400` → `1.7.20.0`,
а `0x01004301` → `1.0.67.1` (ПО стенда). Декодирует `Mvk01Codec.DecodeFirmwareVersion`.

Проверить конкретный канал на перегрузку можно через `DspIndicators`:

```csharp
bool overloaded1 = info.DspIndicators.IsChannelOverloaded(1); // true/false
```

### 2.2.4. Шаг 3. Снимок канала

```csharp
Mvk01Channel channel = await mvk.ReadChannelAsync(1);
```

Один вызов возвращает полный снимок расчётных параметров канала (каналы 1–8):

| Поле | Тип | Смысл |
|---|---|---|
| `Channel` | `int` | номер канала |
| `SensorVoltage` | `float` | напряжение на датчике, В |
| `Spectrum` | `float[]` | 43 полосы дольоктавного спектра |
| `PeakFactor` | `float` | пик-фактор виброускорения X…25 кГц |
| `RmsX25kHzAcceleration` | `float` | СКЗ виброускорения X…25 кГц, м/с2 |
| `RmsSets` | `IReadOnlyList<Mvk01RmsSet>` | 8 наборов `{ Name, Acceleration, Velocity, Displacement }` |
| `DcComponent` | `float` | постоянная составляющая, м/с2 |
| `State` | `Mvk01ChannelState` | перегрузка, незаполненные буферы, выход DC за границы |
| `EquivalentPeakValues` | `float[]` | 24 эквивалентных пиковых значения |
| `EquivalentRangeValues` | `float[]` | 24 эквивалентных значения размаха |

Полезные детали:

- `channel.GetSpectrumBand(bandNumber)` возвращает значение полосы 1–43;
  `Mvk01SpectrumBands.FormatBand(band)` — подпись вида `"10 Гц"`, `"1.25 кГц"`;
  `Mvk01SpectrumBands.CenterFrequencies` — все 43 средние частоты;
- названия наборов СКЗ фиксированы: `10…3000 Гц`, `10…2000 Гц`, `10…1000 Гц`,
  `2…1000 Гц`, `0.8…300 Гц`, `0.8…150 Гц`, `Фильтр 1`, `Фильтр 2`;
- `State` — это запись с булевыми флагами: `Overload`, `Buffer16HzNotFilled`,
  `Buffer64HzNotFilled`, `Buffer512HzNotFilled`, `DcVoltageAboveMaximum`,
  `DcVoltageBelowMinimum`;
- неверный номер канала (0, 9, …) — `ArgumentOutOfRangeException`.

### 2.2.5. Шаг 4. Снимок тахометра

```csharp
Mvk01TachoMeasurement tacho = await mvk.ReadTachoAsync(1);
```

| Поле | Тип | Смысл |
|---|---|---|
| `Tacho` | `int` | номер тахометра (1–2) |
| `FrequencyHz` | `float` | частота вращения, Гц |
| `AverageIncrementHz` | `float` | среднее приращение частоты вращения на оборот, Гц |
| `SensorError` | `bool` | период сигнала меньше минимально допустимого |
| `TimestampSeconds` | `double` | временная метка последнего перехода, с (устройство хранит 1/65536 с) |
| `InputLevel` | `bool` | текущий уровень на входе |
| `Direction` | `Mvk01RotationDirection` | `BelowMinimum`, `Tacho1ToTacho2`, `Tacho2ToTacho1` |
| `TransitionCount` | `uint` | число зарегистрированных переходов 0→1 |

Неверный номер тахометра (0, 3, …) — `ArgumentOutOfRangeException`.

### 2.2.6. Что происходит «под капотом»

`ConnectAsync` **не отправляет Modbus-запросов**: он открывает TCP-соединение
и настраивает таймауты сокета (`ConnectTimeoutMs` = 3000 мс, `ReadTimeoutMs` =
1000 мс, `WriteTimeoutMs` = 3000 мс). Modbus-обмен начинается с первого чтения.

Внутри пула создаётся устройство `MvkDevice` с пайплайном
`timeout → logging → metrics → validation → retry → writeProtection`
и транспортом `NModbusTransport`. Каждый вызов `ReadDeviceInfoAsync`,
`ReadChannelAsync`, `ReadTachoAsync` — это один или несколько запросов
`ReadHoldingRegistersAsync` (функция `0x03`), которые проходят пайплайн и
выполняются **по очереди** (запросы к одному устройству сериализуются).

Состав запросов:

`ReadDeviceInfoAsync()` — 3 запроса:

| Адрес | Регистров | Содержимое |
|---|---|---|
| `0x1000` | 12 | серийный номер (8 байт), версия ПО, системные ошибки, наработка, размер карты памяти |
| `0x100C` | 12 | MD5 ПО DSP (16 байт), состояние агрегата 1, состояние агрегата 2 |
| `0x1020` | 6 | индикаторы DSP: маска перегрузок, флаги конфигурации, счётчик расчётов |

`ReadChannelAsync(1)` — 5 запросов:

| Адрес | Регистров | Содержимое |
|---|---|---|
| `0x2000` | 96 | напряжение, спектр (43 полосы), пик-фактор, СКЗ X…25 кГц |
| `0x2060` | 48 | 8 наборов СКЗ × 3 значения (ускорение, скорость, перемещение) |
| `0x209A` | 6 | постоянная составляющая и состояние канала |
| `0x20A0` | 48 | 24 эквивалентных пиковых значения |
| `0x20E0` | 48 | 24 эквивалентных значения размаха |

Для канала `n` базовый адрес сдвигается: `0x2000 + (n−1)×0x200`. Например,
для канала 8 первый запрос идёт по адресу `0x2E00`.

`ReadTachoAsync(1)` — 1 запрос: `0x3000`, 14 регистров. Для тахометра 2 —
`0x3100`.

`ReadChannelConfigAsync(n)` — 5 запросов из блока конфигурации канала
(`0x100 + (n−1)×0x100`): заголовок (6), единицы (16), серийный номер датчика
(16), фильтры (10), границы DC-напряжения (4).

`ReadMeasurementByNumberAsync(N)` — 1 запрос по адресу
`0x4000 + (N−1)×16`, 16 регистров (8 каналов × 2 регистра): возвращает
`float[8]` — значение параметра для всех каналов сразу. Для N = 48 адрес равен
`0x42F0` (именно этот пример разобран в [MVK01_registers.txt](../MVK01_registers.txt)
с ошибочным адресом `0x41F0`).

Все адреса и количества в примерах чётные — это обязательное требование МВК01.
В `Mvk01Connection` проверка `RequireEvenAddressAndQuantity` включена, поэтому
нечётный запрос отклоняется библиотекой ещё до отправки (ошибка `Validation`),
а не ответом-исключением устройства.

### 2.2.7. Как выглядит вывод

Значения зависят от подключённых датчиков; ниже — пример для стенда
(показаны первые строки и сокращённый список полос):

```
Соединение установлено.
Серийный номер:    00004730
Версия ПО:         1.0.67.1
Наработка:         1.21:07:53
Системные ошибки:  0x00000000 (IsClean = True)
Агрегат 1:         зона Green, карта памяти 1024 КБ

Канал 1: напряжение 7.8213 В
  СКЗ X…25 кГц:      12.250 м/с2
  Пик-фактор:        3.512
  Постоянная сост.:  0.0125 м/с2
  Полос спектра:     43
  Состояние:         перегрузка = False
  СКЗ по полосам:
    10…3000 Гц   уск  12.2500  скорость   1.8400  перемещение  25.4000
    10…2000 Гц   уск  12.2400  скорость   1.8300  перемещение  25.1000
    10…1000 Гц   уск  12.1800  скорость   1.7900  перемещение  24.8000
    ...
  Топ-5 полос спектра:
    25 Гц         0.8421
    50 Гц         0.6174
    100 Гц        0.2310
    12.5 Гц       0.1057
    200 Гц        0.0912

Тахометр 1: 17.130 Гц, направление BelowMinimum, переходов 0, ошибка датчика = False
```

Пояснения к выводу:

- `Errors.Raw = 0x00000000` и `IsClean = True` — системных ошибок нет;
- `Unit1State.Zone` = `Green` — контроль по агрегату 1 в норме;
- `SensorVoltage` ~7.8 В — типичное напряжение питания датчика виброускорения;
- СКЗ 10…1000 Гц — параметр, который чаще всего используется для контроля
  вибрации (номер `Mvk01MeasurementNumber.Rms10To1000Acceleration` = 55).

## 2.3. Уровень 1. Несколько устройств из JSON

Когда устройств больше одного (или нужны нестандартные таймауты, порт, SlaveId),
используется конфигурация. Секция верхнего уровня — `MvkModbus`.

### 2.3.1. Минимальный `mvkmodbus.json`

Обязательные поля устройства — только `Id` и `Host`, остальное берётся из
значений по умолчанию:

```json
{
  "MvkModbus": {
    "Devices": [
      { "Id": "mvk-1", "Host": "10.20.0.211" }
    ]
  }
}
```

Практический вариант для нескольких устройств МВК01:

```json
{
  "MvkModbus": {
    "Defaults": {
      "Port": 502,
      "SlaveId": 1,
      "ByteOrder": "BigEndian",
      "ConnectTimeoutMs": 3000,
      "ReadTimeoutMs": 2000,
      "MaxRetries": 2,
      "ReconnectBaseDelayMs": 500,
      "ReconnectMaxDelayMs": 30000,
      "IdleDisconnectSeconds": 120,
      "RequireEvenAddressAndQuantity": true
    },
    "Pool": {
      "ReconnectEnabled": true,
      "HealthCheckIntervalSeconds": 15
    },
    "Devices": [
      { "Id": "mvk-1", "Name": "МВК01 стенд", "Host": "10.20.0.211" },
      { "Id": "mvk-2", "Name": "МВК01 агрегат 2", "Host": "10.20.0.212", "SlaveId": 2 }
    ]
  }
}
```

Не забудьте скопировать файл в выходной каталог — так делает пример:

```xml
<ItemGroup>
  <None Update="mvkmodbus.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

### 2.3.2. Загрузка пула и чтение

```csharp
using Mvk.Modbus.Pool;
using Mvk.MVK01;

// Пул из JSON-файла: конфигурация загружается и проверяется внутри.
await using MvkDevicePool pool = MvkDevicePool.FromJsonFile("mvkmodbus.json");

// Сырое чтение: типовая обёртка выбрасывает MvkModbusException при ошибке.
IMvkDevice device = pool.GetDevice("mvk-1");
ushort[] version = await device.ReadHoldingRegistersAsync(0x1004, 2);
Console.WriteLine($"ПО: 0x{version[0]:X4}{version[1]:X4}");

// Типизированное чтение: фасад создаётся поверх устройства пула.
Mvk01Device mvk1 = new(device);
Mvk01DeviceInfo info = await mvk1.ReadDeviceInfoAsync();
Console.WriteLine($"{device.Settings.Id} ({device.Settings.Endpoint}): ПО {info.FirmwareVersion}");
```

Если конфигурация нужна и дальше, её можно загрузить явно и создать пул из
объекта настроек:

```csharp
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Pool;

MvkModbusOptions options = MvkModbusOptionsLoader.LoadFromJsonFile("mvkmodbus.json");
await using MvkDevicePool pool = new(options);
```

Что стоит знать:

- `MvkDevicePool.FromJsonFile` объявлен как
  `static MvkDevicePool FromJsonFile(string jsonFilePath, ILoggerFactory? loggerFactory = null)` —
  он синхронный, поэтому `await` при вызове не нужен, а `await using` применяется
  к уже созданному пулу;
- `MvkDevicePool` реализует `IMvkDevicePool` и `IAsyncDisposable`; при освобождении
  закрываются все соединения и останавливается фоновый цикл обслуживания;
- `pool.Devices` — все устройства в виде `IReadOnlyCollection<IMvkDevice>`;
- `GetDevice(id)` бросает `MvkModbusException` с `ErrorKind = DeviceNotFound`,
  если идентификатора нет; `TryGetDevice(id, out device)` — безопасный вариант;
- идентификаторы нечувствительны к регистру (`mvk-1` и `MVK-1` — одно устройство);
- при первом запросе соединение открывается автоматически (ленивое подключение),
  а при обрыве связи работает автореконнект — вручную ничего делать не нужно.

### 2.3.3. Где искать ошибки конфигурации

| Симптом | Где смотреть |
|---|---|
| Файл не найден | `MvkModbusException` (`Configuration`): «Файл конфигурации не найден: 'полный путь'.» |
| Некорректная секция | `MvkModbusException` (`Configuration`): «Некорректная конфигурация 'MvkModbus': …» со списком всех нарушений |
| В DI: ошибка при старте | `OptionsValidationException` со списком всех нарушений |
| Устройство не найдено | `MvkModbusException` (`DeviceNotFound`) из `GetDevice` |
| Устройство отключено | `MvkModbusException` (`DeviceDisabled`) при запросе к `Enabled = false` |
| Неверный адрес/количество | `MvkModbusException` (`Validation`) при `RequireEvenAddressAndQuantity = true` |

Типичные сообщения валидатора (все нарушения возвращаются одним списком):

- «Устройство 'mvk-1': идентификатор Id не уникален.»
- «Устройство 'mvk-1': не задан адрес Host.»
- «Defaults.Port = 0 вне допустимого диапазона 1..65535.»
- «Defaults.ReconnectMaxDelayMs не может быть меньше Defaults.ReconnectBaseDelayMs.»

Порядок работы с ошибкой конфигурации: прочитать текст исключения (там перечислены
**все** нарушения сразу), исправить JSON, перезапустить приложение. Валидация
выполняется до первого обращения к устройству, поэтому «полурабочего» состояния
не бывает.

## 2.4. Уровень 2. Несколько устройств через DI

Когда приложение уже использует хост, конфигурацию и журналирование, удобнее
зарегистрировать библиотеку в DI. Полный сценарий:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Mvk.Modbus.DependencyInjection;
using Mvk.MVK01;
using Mvk.MVK01.DependencyInjection;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "mvkmodbus.json"),
    optional: false,
    reloadOnChange: false);

builder.Services.AddMvkModbus(builder.Configuration);   // пул + конфигурация + валидация
builder.Services.AddMvk01Devices();                     // типизированный слой МВК01
builder.Services.AddMvkModbusHostedService();           // лог состава пула при старте

using IHost host = builder.Build();
await host.StartAsync();

IMvk01DeviceFactory factory = host.Services.GetRequiredService<IMvk01DeviceFactory>();
IMvk01Device device = factory.GetDevice("mvk-1");

Mvk01DeviceInfo info = await device.ReadDeviceInfoAsync();
Mvk01Channel channel = await device.ReadChannelAsync(1);
Console.WriteLine($"{device.Id}: ПО {info.FirmwareVersion}, напряжение {channel.SensorVoltage:F4} В");

await host.StopAsync();
```

Что регистрируется:

- `IMvkDevicePool` / `MvkDevicePool` — singleton;
- `IMvk01DeviceFactory` — singleton (появляется после `AddMvk01Devices()`);
- `IMvkTransportFactory` — `NModbusTransportFactory` (можно заменить своей
  регистрацией — последняя регистрация побеждает);
- hosted-сервис `MvkModbusWarmupHostedService` — только при вызове
  `AddMvkModbusHostedService()`; при старте пишет в лог число устройств и их `Id`.

Важные правила:

- порядок вызовов: сначала `AddMvkModbus`, затем `AddMvk01Devices`;
- `AddMvk01Devices()` через `PostConfigure` включает обязательные для МВК01
  настройки: `RequireEvenAddressAndQuantity = true` и `AllowConfigurationWrite = true`;
- собственные middleware регистрируются до `builder.Build()`:
  `builder.Services.AddModbusMiddleware<AuditMiddleware>("audit")`, а имя `"audit"`
  добавляется в список `Pipeline` (см. [главу 6](06-pipeline-middleware.md));
- `IMvk01DeviceFactory` кэширует фасады: `factory.Devices` возвращает по одному
  `IMvk01Device` на устройство пула.

Подробности конфигурации — в [главе 3](03-configuration.md), пула — в
[главе 5](05-device-pool.md), middleware — в [главе 6](06-pipeline-middleware.md).

## 2.5. Запуск готового примера из репозитория

В репозитории есть полностью собранный пример `samples\Mvk.Sample.Console`
(DI + свой `AuditMiddleware` + чтение канала и спектра):

```powershell
dotnet run --project samples\Mvk.Sample.Console
```

Что показывает разовый запуск:

- состав пула и адрес устройства (`mvk-1`, `10.20.0.211:502`) — в том числе
  строка hosted-сервиса в логах;
- серийный номер, версию ПО и наработку;
- системные ошибки (`Raw`, `IsClean`);
- канал 1: напряжение, пик-фактор, СКЗ X…25 кГц, постоянную составляющую;
- список наборов СКЗ (8 строк) и топ-5 полос спектра;
- тахометр 1: частоту, направление, число переходов;
- итоговое состояние устройства и признак соединения.

Режим периодического опроса:

```powershell
dotnet run --project samples\Mvk.Sample.Console -- watch      # до Ctrl+C
dotnet run --project samples\Mvk.Sample.Console -- watch 10   # 10 секунд
```

Есть и минимальный отдельный пример только с опросом полосы 10…1000 Гц:

```powershell
dotnet run --project samples\Mvk.Sample.Polling                    # до Ctrl+C
dotnet run --project samples\Mvk.Sample.Polling -- 10.20.0.211 10 1 # хост, секунды, канал
```

В режиме `watch` пример каждые 500 мс вызывает
`ReadMeasurementByNumberAsync(Mvk01MeasurementNumber.Rms10To1000Acceleration)`
и печатает СКЗ виброускорения 10…1000 Гц сразу по всем 8 каналам:

```
14:35:02.118  к1=12.1800  к2=11.9400  к3=12.5100  к4=0.0000  к5=0.0000  к6=0.0000  к7=0.0000  к8=0.0000
```

Остановить опрос можно `Ctrl+C` (обрабатывается как отмена) или аргументом с
числом секунд. Транспортные ошибки в этом режиме не прерывают цикл: пример
печатает `ErrorKind` и продолжает опрос — реконнект выполняется фоново.

## 2.6. Типичные проблемы первого запуска

### Файл конфигурации не найден

```
MvkModbusException: Файл конфигурации не найден: 'C:\...\mvkmodbus.json'.
ErrorKind = Configuration
```

Причина: относительный путь разрешается от текущего рабочего каталога, который
зависит от способа запуска. Решение: строить путь от каталога приложения и
копировать файл в выходной каталог:

```csharp
builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "mvkmodbus.json"),
    optional: false,
    reloadOnChange: false);
```

```xml
<None Update="mvkmodbus.json" CopyToOutputDirectory="PreserveNewest" />
```

### Таймаут или нет связи

```
MvkModbusException: Таймаут подключения к 10.20.0.211:502 (3000 мс).
MvkModbusException: Не удалось подключиться к 10.20.0.211:502: <детали сокета>
```

`ErrorKind` = `Timeout` или `Transport`. Что проверить:

1. `Test-NetConnection 10.20.0.211 -Port 502` — доступен ли порт;
2. правильность `Host`/`Port`/`SlaveId` в конфигурации;
3. не блокирует ли соединение брандмауэр или VPN;
4. при медленной сети — увеличить `ConnectTimeoutMs` и `ReadTimeoutMs`.

`Mvk01Connection.ConnectAsync` проверяет связь сразу и при неудаче выбрасывает
исключение; пул после этого освобождается. При работе через пул первый запрос
также подключится автоматически, а при временных ошибках устройство переходит в
`Faulted` и восстанавливается в фоне (см. [главу 4](04-transport-and-raw-registers.md)).

### `DeviceNotFound`

```
MvkModbusException: Устройство 'mvk-9' не найдено в конфигурации пула.
ErrorKind = DeviceNotFound
```

Причина — `GetDevice("mvk-9")` при отсутствии такого `Id` в `Devices`.
Проверьте:

- что загружен тот файл конфигурации (см. выше про `AppContext.BaseDirectory`);
- написание `Id` (регистр не важен, но лишние пробелы обрезаются);
- для необязательных устройств используйте
  `pool.TryGetDevice("mvk-9", out IMvkDevice? device)` или `AddDevice`.

### `OperationCanceledException` при нажатии `Ctrl+C`

Это **ожидаемое** поведение: отмена по `CancellationToken` проявляется как
`OperationCanceledException`, а не как `MvkModbusException`. Если приложению нужно
корректно завершиться (закрыть соединения, дописать лог), перехватите событие и
взведите свой токен:

```csharp
using CancellationTokenSource cts = new();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;   // не убивать процесс мгновенно
    cts.Cancel();
};
```

Без такого обработчика `Ctrl+C` завершает процесс сразу, и `await using` может
не успеть закрыть соединения. В режиме `watch` примера этот сценарий уже
обработан — печатается «Опрос остановлен.».

### Ошибка «Middleware 'x' не найден»

```
MvkModbusException: Middleware 'audit' не найден: нет встроенного имени
и нет регистрации через AddModbusMiddleware<T>(services, "имя").
ErrorKind = Configuration
```

Причина: имя в списке `Pipeline` не совпадает ни с одним встроенным
(`timeout`, `logging`, `metrics`, `validation`, `retry`, `writeProtection`),
ни с зарегистрированным. Решение:

```csharp
builder.Services.AddModbusMiddleware<AuditMiddleware>("audit"); // до builder.Build()
```

Регистрация должна выполняться **до** построения провайдера (`builder.Build()`)
и до создания пула. Имена middleware нечувствительны к регистру. Подробности и
пример своего middleware — в [главе 6](06-pipeline-middleware.md).

### Другие частые замечания

- `OptionsValidationException` при старте хоста — это тот же список нарушений
  конфигурации, только от DI-валидатора; читайте `Failures` целиком.
- Если в консоли «кракозябры» вместо `м/с2` и «…», задайте
  `Console.OutputEncoding = System.Text.Encoding.UTF8;` (строки каналов в МВК01 —
  cp1251, библиотека декодирует их автоматически).
- Неверный номер канала/тахометра/параметра — `ArgumentOutOfRangeException`,
  а не `MvkModbusException`: это ошибка программиста, а не связи.

## 2.7. Что дальше

- [Глава 3. Конфигурация](03-configuration.md) — все поля `Defaults`, `Devices`,
  `Pool`, политика записи и валидация.
- [Глава 4. Транспорт и сырые регистры](04-transport-and-raw-registers.md) —
  подключение, таймауты, состояния, реконнект, `RegisterCodec`.
- [Глава 5. Пул устройств](05-device-pool.md) — `IMvkDevicePool`, `IMvkDevice`,
  сырые регистры, события состояния.
- [Глава 6. Пайплайн middleware](06-pipeline-middleware.md) — порядок
  обработчиков и свои middleware.
- [Глава 7a. Карта регистров и кодеки МВК01](07a-mvk01-register-map-and-codec.md) —
  адреса, формулы и декодирование значений.
- [Глава 7b. Фасад МВК01](07b-mvk01-device-facade.md) — снимки устройства,
  канала и тахометра, параметры по номерам.

## Подводные камни

- **`ConnectAsync` бросает исключение при недоступном устройстве.** Это не
  «ленивое» подключение: проверка связи выполняется сразу. Если нужен цикл
  ожидания — используйте пул и обрабатывайте `MvkModbusException`.
- **Идентификатор устройства в `Mvk01Connection` — это адрес хоста.** Два
  устройства с одинаковым IP, но разными портами/`SlaveId` так подключить
  нельзя — нужна конфигурация с разными `Id`.
- **`ReadTimeoutMs` по умолчанию 1000 мс.** Для медленных сценариев (или на
  нагруженной сети) увеличьте его в конфигурации, как в примере
  `samples\Mvk.Sample.Console\mvkmodbus.json` (там 2000 мс).
- **Снимок канала — не атомарный.** Это 5 последовательных запросов; при
  активном измерении значения между ними могут обновиться. Для согласованного
  набора читайте один параметр по номеру одним запросом.
- **`Mvk01Connection` разрешает запись конфигурации** (`AllowConfigurationWrite = true`),
  потому что иначе не работают `SaveConfigurationAsync` и другие системные
  команды. На реальном устройстве не вызывайте `SaveConfigurationAsync`,
  `RestoreConfigurationAsync`, `RebootAsync` и запись регистров без явного
  разрешения (см. [главу 1, раздел 1.9](01-introduction.md)).
- **При ручной сборке пула включите правило МВК01.** `new MvkDevicePool(options)`
  и `MvkDevicePool.FromJsonFile(...)` не добавляют `RequireEvenAddressAndQuantity`
  автоматически (в отличие от `Mvk01Connection` и `AddMvk01Devices()`); без этого
  нечётный запрос уйдёт на устройство и вернётся ошибкой Modbus `0x01`.
- **Освобождайте пул.** `await using` (или `DisposeAsync`) останавливает фоновый
  цикл обслуживания и закрывает сокеты; без этого приложение может «висеть» на
  завершении.
- **Не создавайте `Mvk01Device` вручную в цикле.** Фасад дешёвый, но в DI-сценарии
  для этого есть кэширующая `IMvk01DeviceFactory`.

## См. также

- [Глава 1. Введение и обзор](01-introduction.md)
- [Глава 3. Конфигурация](03-configuration.md)
- [Глава 7b. Фасад МВК01](07b-mvk01-device-facade.md)
- [README.md](../README.md)
- [PLAN.md](../PLAN.md)
- [AGENTS.md](../AGENTS.md)
