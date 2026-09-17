# 11. Рецепты: типовые задачи

Глава собрана как «поваренная книга»: каждый рецепт — это законченная задача,
готовый к копированию код, пояснение к нему и предостережения. Все API, адреса и
формулы взяты из исходников `src/Mvk.Modbus`, `src/Mvk.MVK01` и проверены тестами
`tests/Mvk.MVK01.Tests`; устройство, на котором проверялись сценарии, —
МВК01 `10.20.0.211:502` (SlaveId 1, BigEndian, ПО 1.0.67.1).

Перед чтением главы полезно освежить:

- [Карта регистров и кодеки](07a-mvk01-register-map-and-codec.md) — адреса, форматы, смещения;
- [Фасад МВК01](07b-mvk01-device-facade.md) — `IMvk01Device`, снимки и команды;
- [Пайплайн middleware](06-pipeline-middleware.md) — таймауты, повторы, защита записи.

Содержание:

1. [Общие предпосылки](#общие-предпосылки)
2. [Периодический опрос одного параметра](#1-периодический-опрос-одного-параметра)
3. [Все 8 каналов одним запросом](#2-все-8-каналов-одним-запросом)
4. [Полный снимок канала](#3-полный-снимок-канала)
5. [Экспорт спектра и СКЗ в CSV](#4-экспорт-спектра-и-скз-в-csv)
6. [Контроль состояния агрегата](#5-контроль-состояния-агрегата)
7. [Сохранение конфигурации во Flash](#6-сохранение-конфигурации-во-flash)
8. [Чтение конфигурации канала](#7-чтение-конфигурации-канала)
9. [Запись настроек канала](#8-запись-настроек-канала)
10. [Мониторинг нескольких устройств](#9-мониторинг-нескольких-устройств)
11. [Журнал состояний устройства](#10-журнал-состояний-устройства)
12. [Тахометр: частота, направление, переходы](#11-тахометр-частота-направление-переходы)
13. [WPF: ViewModel устройства](#12-wpf-viewmodel-устройства)
14. [Диагностика устройства](#13-диагностика-устройства)
15. [Длительная недоступность](#14-длительная-недоступность)
16. [Подводные камни](#подводные-камни)
17. [См. также](#см-также)

---

## Общие предпосылки

Рецепты не повторяют друг друга целиком: ниже два способа получить
`IMvk01Device`, из которых исходят все примеры. Все программы — top-level
или с `Main`, TFM `net8.0`, `Nullable enable`.

**Вариант A — одно устройство, без конфигурации** (`Mvk01Connection` сам создаёт
пул, включает чётность адресов и разрешает запись конфигурации):

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Mvk.Modbus.Pool;
using Mvk.MVK01;

await using Mvk01Connection mvk = await Mvk01Connection.ConnectAsync("10.20.0.211");
IMvk01Device device = mvk.Device;              // типизированный фасад
IMvkDevice raw = mvk.Device.Device;            // сырые регистры и настройки
```

**Вариант B — пул или DI, несколько устройств** (`AddMvk01Devices()` включает те же
настройки МВК01: `RequireEvenAddressAndQuantity` и `AllowConfigurationWrite`):

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Mvk.Modbus.DependencyInjection;
using Mvk.MVK01;
using Mvk.MVK01.DependencyInjection;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile("mvkmodbus.json", optional: false);
builder.Services.AddMvkModbus(builder.Configuration);
builder.Services.AddMvk01Devices();
builder.Services.AddMvkModbusHostedService();

using IHost host = builder.Build();
await host.StartAsync();

IMvk01DeviceFactory factory = host.Services.GetRequiredService<IMvk01DeviceFactory>();
IMvk01Device device = factory.GetDevice("mvk-1");
```

| Сценарий | Что использовать |
|---|---|
| Одно устройство, быстрый старт | `Mvk01Connection.ConnectAsync(host)` |
| Несколько устройств, JSON-конфигурация | `AddMvkModbus` + `AddMvk01Devices` + `IMvk01DeviceFactory` |
| Пул без DI | `new MvkDevicePool(options)` + `new Mvk01Device(pool.GetDevice(id))` (см. рецепт 8) |

Для частых опросов предпочтительны параметры по номеру
(`ReadMeasurementByNumberAsync`) и прямое чтение пары регистров; для редкой
диагностики — снимки `ReadChannelAsync` / `ReadDeviceInfoAsync`, которые делают
по 3–5 запросов.

---

## 1. Периодический опрос одного параметра

**Задача.** Каждые 0,5 с получать СКЗ виброускорения 10…1000 Гц по каналу 1,
корректно завершаться по Ctrl+C и не «падать» при временной потере связи.

**Решение.** `ReadMeasurementByNumberAsync` с `PeriodicTimer`. Номер параметра
`Rms10To1000Acceleration` = 55; вызов возвращает значения всех 8 каналов
одним запросом, канал 1 — это `values[0]`.

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Mvk.Modbus;
using Mvk.MVK01;

internal static class Program
{
    private static async Task Main()
    {
        await using Mvk01Connection mvk = await Mvk01Connection.ConnectAsync("10.20.0.211");
        IMvk01Device device = mvk.Device;

        using CancellationTokenSource cts = new();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;   // не завершать процесс аварийно
            cts.Cancel();
        };

        Console.WriteLine("СКЗ виброускорения 10…1000 Гц, канал 1 (Ctrl+C — стоп):");
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(500));

        try
        {
            while (await timer.WaitForNextTickAsync(cts.Token))
            {
                try
                {
                    float[] values = await device.ReadMeasurementByNumberAsync(
                        Mvk01MeasurementNumber.Rms10To1000Acceleration,
                        cts.Token);

                    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  канал 1: {values[0]:F4} м/с2");
                }
                catch (MvkModbusException ex) when (ex.IsTransient)
                {
                    // Связь: реконнект уже выполняется фоном, продолжаем опрос.
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  нет связи ({ex.ErrorKind}), ждём реконнект");
                }
                catch (MvkModbusException ex)
                {
                    // Данные/конфигурация: без исправления запроса повтор не поможет.
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  ошибка: {ex.ErrorKind} — {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Опрос остановлен.");
        }
    }
}
```

**Вариант «минимальный трафик».** Если нужен только один канал, читаем ровно два
регистра по вычисленному адресу. Смещение набора СКЗ:
`RmsSetOffset + индекс × RmsSetValues × 2`, где индекс 2 — полоса 10…1000 Гц.
Для канала 1 это `0x2000 + 0x60 + 12 = 0x206C` (проверено на устройстве).

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Mvk.Modbus;
using Mvk.Modbus.Pool;
using Mvk.Modbus.Protocol;
using Mvk.MVK01;

internal static class Program
{
    // 0x2000 + 0x60 + 2 * 3 * 2 = 0x206C — СКЗ 10…1000 Гц (ускорение) канала 1.
    private static readonly ushort Rms10To1000Address = (ushort)(
        Mvk01RegisterMap.ChannelData(1)
        + Mvk01RegisterMap.RmsSetOffset
        + 2 * Mvk01RegisterMap.RmsSetValues * 2);

    private static async Task Main()
    {
        await using Mvk01Connection mvk = await Mvk01Connection.ConnectAsync("10.20.0.211");
        IMvkDevice raw = mvk.Device.Device;

        using CancellationTokenSource cts = new();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(500));
        try
        {
            while (await timer.WaitForNextTickAsync(cts.Token))
            {
                try
                {
                    ushort[] registers = await raw.ReadHoldingRegistersAsync(
                        Rms10To1000Address, 2, cts.Token);

                    float value = RegisterCodec.ToSingle(registers, raw.Settings.ByteOrder);
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  10…1000 Гц: {value:F4} м/с2");
                }
                catch (MvkModbusException ex) when (ex.IsTransient)
                {
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  нет связи ({ex.ErrorKind})");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
```

**Пояснение.** `ReadMeasurementByNumberAsync(55)` — один запрос 16 регистров по
адресу `MeasurementByNumber(55)` = `0x4000 + (55−1)×16 = 0x4360`, поэтому он
дешевле снимка канала из 5 запросов, но не даёт напряжения, спектра и состояний.
Прямое чтение 2 регистров экономит ещё в 8 раз, но привязано к одному каналу:
для канала N адрес считается по той же формуле от `ChannelData(N)`.

**Предостережения.**

- Порядок байт берётся из `raw.Settings.ByteOrder`; «жёсткий» `BitConverter`
  без учёта `BigEndian`/`Mixed`/`LittleEndian` даст мусор вместо значения.
- 0x206C чётный, количество 2 — чётное: требование МВК01 выполнено. При ручном
  расчёте адреса следите за чётностью (нечётный адрес — ошибка Modbus `0x01`).
- `RmsSets[2]` и параметр 55 — это одно и то же значение; параметр 48
  (`RmsX25kHzAcceleration`) — другое (полоса X…25 кГц).
- Не запускайте два-три таких цикла на одно устройство «для скорости»: запросы
  сериализуются (`MaxConcurrentRequests = 1`), а период будет дрейфовать.
- После `cts.Cancel()` цикл завершается через `OperationCanceledException`,
  а не через `MvkModbusException`; не перехватывайте его как ошибку связи.

---

## 2. Все 8 каналов одним запросом

**Задача.** Прочитать один параметр сразу по всем восьми каналам и вывести
таблицу: например, СКЗ X…25 кГц (N = 48), СКЗ 10…1000 Гц (N = 55) и напряжение
(N = 1). При необходимости — прочитать «сырое» битовое поле.

**Решение.** `ReadMeasurementByNumberAsync` возвращает `float[8]`;
`ReadMeasurementRawByNumberAsync` — `ushort[16]` без декодирования.

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using Mvk.Modbus.Protocol;
using Mvk.MVK01;

internal static class Program
{
    private static async Task Main()
    {
        await using Mvk01Connection mvk = await Mvk01Connection.ConnectAsync("10.20.0.211");
        IMvk01Device device = mvk.Device;

        PrintRow(
            "N=48  СКЗ виброускорения X…25 кГц, м/с2",
            await device.ReadMeasurementByNumberAsync(Mvk01MeasurementNumber.RmsX25kHzAcceleration));

        PrintRow(
            "N=55  СКЗ виброускорения 10…1000 Гц, м/с2",
            await device.ReadMeasurementByNumberAsync(Mvk01MeasurementNumber.Rms10To1000Acceleration));

        PrintRow(
            "N=1   Напряжение на датчике, В",
            await device.ReadMeasurementByNumberAsync(Mvk01MeasurementNumber.SensorVoltage));

        // Любой параметр можно прочитать числовым номером (1–184).
        PrintRow(
            "N=57  СКЗ виброперемещения 10…1000 Гц, мкм",
            await device.ReadMeasurementByNumberAsync(57));

        // Битовые поля без декодирования: параметр 80 — состояние канала.
        ushort[] raw = await device.ReadMeasurementRawByNumberAsync(Mvk01MeasurementNumber.ChannelState);
        Mvk01ChannelState channel1 = Mvk01Codec.DecodeChannelState(
            RegisterCodec.ToUInt32(raw.AsSpan(0, 2), device.Settings.ByteOrder));

        Console.WriteLine($"Состояние канала 1: перегрузка — {channel1.Overload}, "
            + $"DC выше макс. — {channel1.DcVoltageAboveMaximum} (0x{channel1.Raw:X8})");
    }

    private static void PrintRow(string title, float[] values)
    {
        Console.WriteLine(title);
        Console.WriteLine("  канал    " + string.Join("  ",
            Enumerable.Range(1, values.Length).Select(channel => $"{channel,8}")));
        Console.WriteLine("  значение " + string.Join("  ",
            values.Select(value => $"{value,8:F4}")));
        Console.WriteLine();
    }
}
```

Пример вывода:

```
N=48  СКЗ виброускорения X…25 кГц, м/с2
  канал           1         2         3         4         5         6         7         8
  значение   1.2345    1.1980    0.9477    1.3012    1.1150    0.9981    1.2044    1.0763
```

**Пояснение.** Параметры по номерам (раздел 5.1.2 карты) лежат одной строкой:
восемь каналов подряд, по два регистра на канал. Адрес параметра N:
`0x4000 + (N−1)×16`, для N = 48 это `0x42F0` (в `MVK01_registers.txt` приведён
ошибочный `0x41F0` — библиотека использует формулу, см. тест
`Mvk01RegisterMapTests`). Значения соответствуют одноимённым полям
`Mvk01Channel` из раздела 5.1.1.

**Предостережения.**

- Адресация по номерам доступна только в ПО 1.0.61 и новее; для более старых
  версий читайте параметры по каналам через `ReadChannelAsync`.
- Номера 105–112, 137–152 и 173–184 — резерв или служебные поля; таблица
  разделов 5.1.1/5.1.2 не для всех номеров содержит осмысленные данные.
- `ReadMeasurementRawByNumberAsync` возвращает регистры в порядке устройства:
  для канала 1 это `raw[0..1]`, для канала 8 — `raw[14..15]`; декодируйте с
  `device.Settings.ByteOrder`.
- Не пытайтесь читать несколько параметров одним запросом «по диагонали»: шаг
  16 регистров, соседний параметр начинается там же, где заканчивается блок
  каналов предыдущего.

---

## 3. Полный снимок канала

**Задача.** Раз в несколько секунд получать по каналу 1 всё сразу: напряжение,
топ-5 полос спектра, все наборы СКЗ, состояния — и печатать человекочитаемую
сводку.

**Решение.** `ReadChannelAsync` — один вызов, внутри 5 запросов к устройству.

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using Mvk.MVK01;

internal static class Program
{
    private static async Task Main()
    {
        await using Mvk01Connection mvk = await Mvk01Connection.ConnectAsync("10.20.0.211");
        Mvk01Channel channel = await mvk.Device.ReadChannelAsync(1);

        Console.WriteLine($"Канал {channel.Channel}");
        Console.WriteLine($"  Напряжение на датчике : {channel.SensorVoltage,9:F4} В");
        Console.WriteLine($"  Постоянная составляющая: {channel.DcComponent,8:F4} м/с2");
        Console.WriteLine($"  Пик-фактор X…25 кГц   : {channel.PeakFactor,9:F3}");
        Console.WriteLine($"  СКЗ X…25 кГц          : {channel.RmsX25kHzAcceleration,9:F3} м/с2");

        Console.WriteLine("  Топ-5 полос спектра:");
        foreach ((int band, float value) in channel.Spectrum
            .Select((value, index) => (Band: index + 1, Value: value))
            .OrderByDescending(item => item.Value)
            .Take(5))
        {
            Console.WriteLine($"    {Mvk01SpectrumBands.FormatBand(band),-10} {value,10:F4}");
        }

        Console.WriteLine("  Наборы СКЗ (ускорение / скорость / перемещение):");
        foreach (Mvk01RmsSet set in channel.RmsSets)
        {
            Console.WriteLine($"    {set.Name,-12} "
                + $"{set.Acceleration,8:F3} м/с2  {set.Velocity,8:F3} мм/с  {set.Displacement,9:F1} мкм");
        }

        Mvk01ChannelState state = channel.State;
        Console.WriteLine($"  Состояние: перегрузка — {state.Overload}; "
            + $"буферы не заполнены — {state.Buffer16HzNotFilled || state.Buffer64HzNotFilled || state.Buffer512HzNotFilled}; "
            + $"DC выше макс. — {state.DcVoltageAboveMaximum}; DC ниже мин. — {state.DcVoltageBelowMinimum}");
    }
}
```

**Пояснение.** Снимок собирается из пяти чтений: основной блок `A+0x00` (0x60
регистров: напряжение, спектр, пик-фактор, СКЗ X…25 кГц), наборы СКЗ `A+0x60`
(48 регистров), DC/состояние `A+0x9A` (6 регистров), эквивалентные пиковые
`A+0xA0` (48) и размах `A+0xE0` (48). Полосы спектра нумеруются 1–43, средние
частоты — `Mvk01SpectrumBands.CenterFrequencies`; подпись даёт
`FormatBand(band)` («10 Гц», «1.25 кГц»). Тот же результат без фасада —
набор отдельных чтений, как в [07a](07a-mvk01-register-map-and-codec.md).

**Предостережения.**

- Снимок — самый «тяжёлый» вызов: не опрашивайте его чаще, чем раз в 1–2 с,
  и не запускайте параллельно с другими чтениями того же устройства.
- Топ-5 по «тихому» каналу может состоять из околонулевых полос — это нормально,
  фильтруйте `value > 0.001f`, если нужен содержательный вывод.
- `FormatBand` форматирует число в текущей культуре: «1,25 кГц» в русской.
  Для файлов и парсинга используйте `CenterFrequencies` и инвариантную культуру.
- Эквивалентные значения (`EquivalentPeakValues`/`EquivalentRangeValues`) — это
  не наборы СКЗ; не путайте их с `RmsSets` (см. подсказки в конце главы).

---

## 4. Экспорт спектра и СКЗ в CSV

**Задача.** Сохранить снимок канала в два CSV-файла — спектр (43 полосы) и
наборы СКЗ (8 × 3) — так, чтобы файлы корректно открывались в Excel и не
зависели от локали.

**Решение.** Разделитель `;` (привычен русскому Excel), UTF-8 с BOM, все числа —
через `CultureInfo.InvariantCulture`.

```csharp
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Mvk.MVK01;

internal static class Program
{
    private static async Task Main()
    {
        await using Mvk01Connection mvk = await Mvk01Connection.ConnectAsync("10.20.0.211");
        Mvk01Channel channel = await mvk.Device.ReadChannelAsync(1);

        string spectrumPath = $"mvk01-channel{channel.Channel}-spectrum.csv";
        string rmsPath = $"mvk01-channel{channel.Channel}-rms.csv";

        await ExportSpectrumAsync(channel, spectrumPath);
        await ExportRmsSetsAsync(channel, rmsPath);

        Console.WriteLine($"Записано: {Path.GetFullPath(spectrumPath)}");
        Console.WriteLine($"Записано: {Path.GetFullPath(rmsPath)}");
    }

    /// <summary>Экспорт дольоктавного спектра: полоса, средняя частота, значение.</summary>
    private static async Task ExportSpectrumAsync(Mvk01Channel channel, string path)
    {
        StringBuilder csv = new();
        csv.AppendLine("band;center_frequency_hz;value");

        for (int band = 1; band <= channel.Spectrum.Length; band++)
        {
            double frequency = Mvk01SpectrumBands.GetCenterFrequency(band);

            csv.Append(band.ToString(CultureInfo.InvariantCulture)).Append(';')
               .Append(frequency.ToString("0.######", CultureInfo.InvariantCulture)).Append(';')
               .Append(channel.Spectrum[band - 1].ToString("R", CultureInfo.InvariantCulture))
               .AppendLine();
        }

        await File.WriteAllTextAsync(
            path, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    /// <summary>Экспорт наборов СКЗ: полоса, ускорение, скорость, перемещение.</summary>
    private static async Task ExportRmsSetsAsync(Mvk01Channel channel, string path)
    {
        StringBuilder csv = new();
        csv.AppendLine("band;acceleration_m_s2;velocity_mm_s;displacement_um");

        foreach (Mvk01RmsSet set in channel.RmsSets)
        {
            csv.Append(Escape(set.Name)).Append(';')
               .Append(set.Acceleration.ToString("R", CultureInfo.InvariantCulture)).Append(';')
               .Append(set.Velocity.ToString("R", CultureInfo.InvariantCulture)).Append(';')
               .Append(set.Displacement.ToString("R", CultureInfo.InvariantCulture))
               .AppendLine();
        }

        await File.WriteAllTextAsync(
            path, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    /// <summary>Экранирование поля CSV, если в нём есть разделитель или кавычки.</summary>
    private static string Escape(string value)
    {
        return value.Contains(';') || value.Contains('"')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }
}
```

Пример содержимого `mvk01-channel1-rms.csv`:

```
band;acceleration_m_s2;velocity_mm_s;displacement_um
10…3000 Гц;1.2345;2.3456;45.6
10…2000 Гц;1.198;2.31;44.9
...
```

**Пояснение.** Формат `"R"` даёт кратчайшее значение, которое читается обратно
без потерь; для отображения можно взять `F4`/`F6`. Инвариантная культура важна:
без неё `1,25` в русской локали превратится в `1,25` с запятой-разделителем и
сломает колонки; кроме того, `Mvk01SpectrumBands.FormatBand` использует текущую
культуру и для CSV не подходит. Имя полосы СКЗ содержит символ «…», а не запятую,
поэтому `Escape` здесь нужен только для полноты.

**Предостережения.**

- Для Excel с русской локалью разделитель `;` удобнее `,`; выберите один вариант
  и придерживайтесь его во всех файлах.
- UTF-8 с BOM — чтобы Excel не решил, что файл в ANSI, и не испортил «м/с2».
- Если экспортируете несколько снимков подряд, добавляйте метку времени в имя
  файла и не перезаписывайте предыдущий молча.
- Не экспортируйте `float.NaN`/`Infinity` без пометки — Excel покажет `#ЧИСЛО!`;
  проверяйте `float.IsFinite(value)`.

---

## 5. Контроль состояния агрегата

**Задача.** Следить за зонами контроля агрегатов 1 и 2, замечать переходы
в жёлтую/красную зону и отказ датчика.

**Решение.** Состояние приходит в `Mvk01DeviceInfo.Unit1State`/`Unit2State`
(регистры 0x1014/0x1016). Код отслеживает смену зоны и печатает события.

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Mvk.MVK01;

internal static class Program
{
    private static async Task Main()
    {
        await using Mvk01Connection mvk = await Mvk01Connection.ConnectAsync("10.20.0.211");
        IMvk01Device device = mvk.Device;

        using CancellationTokenSource cts = new();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        Mvk01Zone previousZone = Mvk01Zone.Undefined;
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));

        try
        {
            while (await timer.WaitForNextTickAsync(cts.Token))
            {
                Mvk01DeviceInfo info = await device.ReadDeviceInfoAsync(cts.Token);
                Mvk01UnitState unit1 = info.Unit1State;

                if (unit1.Zone != previousZone)
                {
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss}  агрегат 1: "
                        + $"{DescribeZone(previousZone)} -> {DescribeZone(unit1.Zone)}");
                    previousZone = unit1.Zone;
                }

                if (unit1.SensorFailureEvent)
                {
                    Console.WriteLine("  событие: зафиксирован отказ датчика (бит 9)");
                }

                if (unit1.EnteredYellowZone)
                {
                    Console.WriteLine("  событие: был переход в жёлтую зону (бит 10)");
                }

                if (unit1.EnteredRedZone)
                {
                    Console.WriteLine("  событие: был переход в красную зону (бит 11)");
                }

                Console.WriteLine($"  агрегат 2: {DescribeZone(info.Unit2State.Zone)} "
                    + $"(0x{info.Unit2State.Raw:X8})");
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static string DescribeZone(Mvk01Zone zone)
    {
        return zone switch
        {
            Mvk01Zone.Green => "Green (норма)",
            Mvk01Zone.Yellow => "Yellow (предупреждение)",
            Mvk01Zone.Red => "Red (авария)",
            Mvk01Zone.SensorFailure => "SensorFailure (отказ датчика)",
            Mvk01Zone.InvalidConfiguration => "InvalidConfiguration",
            _ => "Undefined (зона не определена)"
        };
    }
}
```

**Вариант «минимальный трафик».** Оба агрегата читаются одним запросом четырёх
регистров 0x1014–0x1017:

```csharp
using System;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Protocol;
using Mvk.MVK01;

ushort[] raw = await device.Device.ReadHoldingRegistersAsync(
    Mvk01RegisterMap.Unit1ControlState, 4);                     // 0x1014, 4 регистра

MvkByteOrder order = device.Settings.ByteOrder;
Mvk01UnitState unit1 = Mvk01Codec.DecodeUnitState(
    RegisterCodec.ToUInt32(raw.AsSpan(0, 2), order));
Mvk01UnitState unit2 = Mvk01Codec.DecodeUnitState(
    RegisterCodec.ToUInt32(raw.AsSpan(2, 2), order));
```

**Пояснение.** Формат 4.1.4: биты 0–7 — номер зоны (`0x11` Green, `0x12`
SensorFailure, `0x13` InvalidConfiguration, `0x14` Yellow, `0x15` Red, `0x00`
Undefined), биты 9–11 — события, остальные биты — резерв. Реакция зависит от
зоны: `Red` — аварийная сигнализация и останов, `Yellow` — предупреждение и
контроль тренда, `SensorFailure` — проверка датчика и кабеля,
`InvalidConfiguration` — проверка настройки контролируемых параметров.

**Предостережения.**

- Биты 9–11 — это «был переход», а не текущее состояние: не поднимайте аварию
  только по ним, если зона уже вернулась в Green. Отдельной команды сброса
  этих битов документация не описывает — храните предыдущую зону у себя
  (как в примере) и ориентируйтесь на неё.
- `Raw` содержит не только зону: не сравнивайте `Raw` с `0x11`/`0x14`, только
  `Zone` (младший байт).
- `ReadDeviceInfoAsync` делает три запроса (включая MD5 и строку); для частого
  контроля зоны используйте прямой запрос 0x1014 из варианта выше.
- Не вызывайте методы устройства из обработчиков событий синхронно — см.
  предостережения рецепта 10.

---

## 6. Сохранение конфигурации во Flash

**Задача.** Выполнить `SaveConfiguration` и корректно обработать статусы:
успех, ошибка Flash, неверная команда, таймаут, ошибка транспорта.

**Решение.** `SaveConfigurationAsync()` пишет команду `0xF0001` в регистр 0x0002
и опрашивает статус каждые 200 мс (общий таймаут 3 с).

> **Внимание.** На реальном устройстве Save/Reboot/Restore выполняйте только
> с явного разрешения ответственного за стенд: команда изменяет содержимое
> энергонезависимой памяти и влияет на работу агрегата.

```csharp
using System;
using System.Threading.Tasks;
using Mvk.Modbus.Protocol;
using Mvk.MVK01;

internal static class Program
{
    private static async Task Main()
    {
        await using Mvk01Connection mvk = await Mvk01Connection.ConnectAsync("10.20.0.211");

        Mvk01CommandResult result = await mvk.SaveConfigurationAsync();

        if (result.IsSuccess)
        {
            Console.WriteLine("Конфигурация сохранена во Flash.");
            return;
        }

        string deviceStatus = result.DeviceStatus is uint status ? $"0x{status:X5}" : "нет";
        Console.WriteLine($"Не сохранено: {result.Status} (статус устройства: {deviceStatus})");

        switch (result.Status)
        {
            case Mvk01CommandStatus.FlashError:
                Console.WriteLine("Ошибка Flash: конфигурация НЕ сохранена. "
                    + "Проверьте карту памяти и питание, затем повторите команду.");
                break;

            case Mvk01CommandStatus.BadCommand:
                Console.WriteLine("Устройство отклонило команду (0x00BAD). "
                    + "Проверьте версию ПО: команды поддерживаются с 1.0.61.");
                break;

            case Mvk01CommandStatus.Timeout:
            {
                ushort[] registers = await mvk.Device.Device.ReadHoldingRegistersAsync(
                    Mvk01RegisterMap.SystemCommand, 2);
                uint deviceStatusNow = RegisterCodec.ToUInt32(registers, mvk.Device.Settings.ByteOrder);

                Console.WriteLine($"Статус не дождались за 3 с; регистр 0x0002 = 0x{deviceStatusNow:X5}. "
                    + "Команда может ещё выполняться — проверьте статус повторно.");
                break;
            }

            case Mvk01CommandStatus.TransportError:
                Console.WriteLine($"Связь потеряна: {result.Error?.ErrorKind}. "
                    + "Дождитесь автоматического реконнекта и повторите команду.");
                break;

            case Mvk01CommandStatus.Pending:
                Console.WriteLine("Команда принята, статус ещё не определён.");
                break;
        }
    }
}
```

**Пояснение.** Команда кодируется `Mvk01Codec.EncodeSystemCommand` (BigEndian:
`0x000F, 0x0001`) и записывается по 0x0002 функцией 0x10. Успех — `0x00000`,
неверная команда — `0x00BAD`, ошибка Flash — `0xF0BAD`. Через `Mvk01Connection`
и после `AddMvk01Devices()` запись разрешена (`AllowConfigurationWrite = true`),
иначе middleware `writeProtection` отклонит запрос ещё до устройства. Если
3 секунд не хватает, увеличьте таймаут явно:

```csharp
Mvk01CommandResult slow = await mvk.ExecuteSystemCommandAsync(
    Mvk01SystemCommand.SaveConfiguration, TimeSpan.FromSeconds(10));
```

**Предостережения.**

- `Timeout` не означает «не сохранено»: устройство могло не успеть вернуть
  статус. Прочитайте 0x0002 ещё раз, прежде чем повторять команду.
- После `FlashError` повтор обычно не помогает — сначала устраните причину
  (карта памяти, питание), иначе получите ту же ошибку.
- `RebootAsync()` разрывает соединение: после перезапуска подождите несколько
  секунд и переподключитесь, чтение в этот момент вернёт транспортную ошибку.
- `RestoreConfigurationAsync()` затрёт текущие настройки значениями из Flash —
  не вызывайте его «на всякий случай».
- Проверьте состояние перед сохранением: `ReadDeviceInfoAsync().Errors.IsClean`
  и зоны агрегатов помогут понять, стоит ли фиксировать конфигурацию.

---

## 7. Чтение конфигурации канала

**Задача.** Прочитать настройки канала (коэффициент, порог перегрузки, единицы,
серийный номер, фильтры, допустимые напряжения) и вывести человекочитаемую
сводку.

**Решение.** `ReadChannelConfigAsync(channel)`.

```csharp
using System;
using System.Threading.Tasks;
using Mvk.MVK01;

internal static class Program
{
    private static async Task Main()
    {
        await using Mvk01Connection mvk = await Mvk01Connection.ConnectAsync("10.20.0.211");
        Mvk01ChannelConfig config = await mvk.ReadChannelConfigAsync(1);

        Console.WriteLine($"Канал {config.Channel}");
        Console.WriteLine($"  Тип канала             : {config.ChannelType}");
        Console.WriteLine($"  Коэффициент датчика    : {config.SensorFactor:F6} В/(м/с2)");
        Console.WriteLine($"  Порог перегрузки       : {config.OverloadThreshold:F4} (1.0 = максимум)");
        Console.WriteLine($"  Единицы измерения      : {config.Units}");
        Console.WriteLine($"  Серийный номер датчика : {config.SerialNumber}");
        Console.WriteLine($"  Фильтр X…25 кГц        : {DescribeFilter(config.FilterX25kHz)}");
        Console.WriteLine($"  Фильтр 1               : {DescribeBands(config.Filter1LowBand, config.Filter1HighBand)}");
        Console.WriteLine($"  Фильтр 2               : {DescribeBands(config.Filter2LowBand, config.Filter2HighBand)}");
        Console.WriteLine($"  Пост. напряжение       : от {config.MinDcVoltage:F3} до {config.MaxDcVoltage:F3} В");
    }

    private static string DescribeFilter(Mvk01FilterX25kHz filter)
    {
        return filter switch
        {
            Mvk01FilterX25kHz.Off => "выключен",
            Mvk01FilterX25kHz.Hz6400 => "6.4 кГц",
            Mvk01FilterX25kHz.Hz10000 => "10 кГц",
            Mvk01FilterX25kHz.Hz15000 => "15 кГц",
            _ => $"неизвестно ({(uint)filter})"
        };
    }

    private static string DescribeBands(int lowBand, int highBand)
    {
        if (lowBand is < 1 or > Mvk01RegisterMap.SpectrumBands
            || highBand is < 1 or > Mvk01RegisterMap.SpectrumBands)
        {
            return $"номера полос {lowBand}…{highBand} (вне диапазона 1…{Mvk01RegisterMap.SpectrumBands})";
        }

        return $"{Mvk01SpectrumBands.FormatBand(lowBand)} … {Mvk01SpectrumBands.FormatBand(highBand)}";
    }
}
```

**Пояснение.** Конфигурация канала — блок 0x100 регистров, база
`ChannelConfig(N) = 0x100 + (N−1)×0x100`. Строки единиц и серийного номера
хранятся с обратным порядком байт внутри 4-байтных слов и в cp1251
(`DecodeTextSwapped`), поэтому `Mvk01Connection` или фабрика с
`Mvk01Codec.DefaultTextEncoding` обязательны для кириллицы. Номера полос
фильтров 1 и 2 — это номера дольоктав 1–43 (раздел 5.1.3): `FormatBand`
превращает их в «6.3 Гц», «1.25 кГц» и т.п.

**Предостережения.**

- `ReadChannelConfigAsync` выполняет 5 запросов; не вызывайте его в цикле
  500 мс — для контроля лучше читать конкретные регистры.
- Коэффициент пересчёта по смещению `+0x0E` — резервный, устройство считает его
  само; в снимке `Mvk01ChannelConfig` он намеренно отсутствует, и записывать
  туда ничего нельзя.
- Порог перегрузки — в долях (1.0 = максимальная амплитуда), а не в вольтах
  или м/с2; не путайте с `MaxDcVoltage`/`MinDcVoltage`.
- Кодировка строк по умолчанию — cp1251 (`Mvk01Codec.DefaultTextEncoding`);
  если провайдер кодовых страниц недоступен, происходит откат в Latin1, и вместо
  «м/с2» получится «ì/ñ2».
- Значения фильтров, равные 0 или выходящие за 1–43, выводятся как есть —
  трактуйте их как «не настроено», прежде чем считать формулу полосы.

---

## 8. Запись настроек канала

**Задача.** Аккуратно изменить порог перегрузки канала 1: с проверкой прав
записи, чётности, результата и с возможностью вернуть прежнее значение.

**Решение.** Запись двух регистров по адресу `ChannelConfig(1) + 0x04 = 0x0104`
через `ExecuteAsync` (ответ, а не исключение) с последующей проверкой чтением.

```csharp
using System;
using System.Threading.Tasks;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Pool;
using Mvk.Modbus.Protocol;
using Mvk.MVK01;

internal static class Program
{
    private static async Task Main()
    {
        // Запись в область 0x0000–0x0FFF разрешена только при AllowConfigurationWrite = true;
        // RequireEvenAddressAndQuantity обязателен для МВК01.
        MvkModbusOptions options = new()
        {
            Defaults = new MvkDefaultsOptions
            {
                RequireEvenAddressAndQuantity = true,
                AllowConfigurationWrite = true
            },
            Pool = new MvkPoolOptions { ReconnectEnabled = true },
            Devices = [new MvkDeviceOptions { Id = "mvk-1", Host = "10.20.0.211" }]
        };

        await using MvkDevicePool pool = new(options);
        IMvk01Device device = new Mvk01Device(pool.GetDevice("mvk-1"));

        const int channel = 1;
        const float newThreshold = 0.9f;   // в долях: 1.0 = максимальная амплитуда

        ushort address = (ushort)(Mvk01RegisterMap.ChannelConfig(channel)
            + Mvk01RegisterMap.ChannelOverloadThresholdOffset);          // 0x0104

        Mvk01ChannelConfig before = await device.ReadChannelConfigAsync(channel);
        Console.WriteLine($"Порог до записи: {before.OverloadThreshold:F4}");

        ushort[] payload = RegisterCodec.FromSingle(newThreshold, device.Settings.ByteOrder);
        ModbusResponse response = await device.Device.ExecuteAsync(
            ModbusRequest.WriteMultipleRegisters(device.Id, address, payload));

        if (!response.IsSuccess)
        {
            Console.WriteLine($"Запись не выполнена: {response.Error?.ErrorKind} — {response.Error?.Message}");
            return;
        }

        Mvk01ChannelConfig after = await device.ReadChannelConfigAsync(channel);
        Console.WriteLine($"Порог после записи: {after.OverloadThreshold:F4} (ожидали {newThreshold:F4})");

        if (Math.Abs(after.OverloadThreshold - newThreshold) > 0.0001f)
        {
            Console.WriteLine("Значение не совпало — возвращаю прежний порог.");
            await device.Device.WriteMultipleRegistersAsync(
                address, RegisterCodec.FromSingle(before.OverloadThreshold, device.Settings.ByteOrder));
            return;
        }

        Console.WriteLine("Готово. Для сохранения во Flash вызовите SaveConfigurationAsync().");
    }
}
```

**Пояснение.** Запись выполняется ровно двумя регистрами (float = 4 байта):
`0x0104` — чётный адрес, количество 2 — чётное. `FromSingle` учитывает
`ByteOrder` устройства. `ExecuteAsync` возвращает `ModbusResponse` с ошибкой
внутри, поэтому проверяем `IsSuccess`; обёртка
`WriteMultipleRegistersAsync` в этой же ситуации бросила бы
`MvkModbusException`. Измененная конфигурация действует сразу, но после
перезапуска устройства пропадёт, если не выполнить `SaveConfigurationAsync`.

**Предостережения.**

- **Запись только с разрешения.** Изменение конфигурации влияет на контроль
  агрегата: согласуйте значения с ответственным за эксплуатацию.
- Не пишите в `+0x0E` (резервный коэффициент), `0x0000` (конфигурация DSP)
  и `0x000E` (частота дискретизации) — документация помечает их как «не писать».
- Строки (`Units`, `SerialNumber`) записывайте только через
  `Mvk01Codec.EncodeTextSwapped` и с той же кодировкой, что использует
  устройство; иначе единицы измерения превратятся в мусор.
- Читайте значение до записи: если новая настройка окажется ошибочной (например,
  порог 0.01 приведёт к постоянным срабатываниям), у вас будет чем откатиться.
- Устройство может не валидировать значения: оно примет и опасный порог,
  и чужие единицы — проверка на стороне приложения обязательна.
- `AllowConfigurationWrite = true` открывает запись **всей** защищённой области
  0x0000–0x0FFF, включая IP-адрес и порт; не включайте его «на всякий случай».

---

## 9. Мониторинг нескольких устройств

**Задача.** Раз в секунду опрашивать несколько МВК01 и выводить сводную таблицу
«устройство — состояние — значение», не останавливаясь при ошибках одного из них.

**Решение.** Пул устройств + `Mvk01DeviceFactory`, последовательный обход.

```csharp
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mvk.Modbus;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Pool;
using Mvk.MVK01;

internal static class Program
{
    private static async Task Main()
    {
        MvkModbusOptions options = new()
        {
            Defaults = new MvkDefaultsOptions { RequireEvenAddressAndQuantity = true },
            Pool = new MvkPoolOptions { ReconnectEnabled = true, HealthCheckIntervalSeconds = 15 },
            Devices =
            [
                new MvkDeviceOptions { Id = "mvk-1", Name = "Агрегат 1", Host = "10.20.0.211" },
                new MvkDeviceOptions { Id = "mvk-2", Name = "Агрегат 2", Host = "10.20.0.212" },
                new MvkDeviceOptions { Id = "mvk-3", Name = "Агрегат 3", Host = "10.20.0.213" }
            ]
        };

        await using MvkDevicePool pool = new(options);

        // Фабрика кэширует фасады; кодировка нужна для русских строк в конфигурации.
        IMvk01DeviceFactory factory = new Mvk01DeviceFactory(pool, Mvk01Codec.DefaultTextEncoding);
        IMvk01Device[] devices = factory.Devices.OrderBy(item => item.Id).ToArray();

        using CancellationTokenSource cts = new();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cts.Token))
            {
                Console.WriteLine($"--- {DateTime.Now:HH:mm:ss} ---");
                Console.WriteLine($"{"Устройство",-12} {"Состояние",-13} {"Канал 1: СКЗ 10…1000 Гц",24}");

                foreach (IMvk01Device device in devices)
                {
                    string value;

                    try
                    {
                        float[] values = await device.ReadMeasurementByNumberAsync(
                            Mvk01MeasurementNumber.Rms10To1000Acceleration, cts.Token);
                        value = values[0].ToString("F4");
                    }
                    catch (MvkModbusException ex) when (ex.IsTransient)
                    {
                        value = "нет связи";
                    }
                    catch (MvkModbusException ex)
                    {
                        value = $"ошибка {ex.ErrorKind}";
                    }

                    Console.WriteLine($"{device.Id,-12} {device.State,-13} {value,24}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
```

Пример вывода:

```
--- 14:32:05 ---
Устройство   Состояние     Канал 1: СКЗ 10…1000 Гц
mvk-1        Connected                       1.2345
mvk-2        Faulted                       нет связи
mvk-3        Connected                       0.9812
```

**Пояснение.** Каждый вызов `ReadMeasurementByNumberAsync` — это один запрос
16 регистров, поэтому таблица из десяти устройств стоит 10 запросов в секунду.
Устройства пула независимы: ошибка `mvk-2` не влияет на `mvk-1` и `mvk-3`,
а реконнект `Faulted`-устройства выполняет фоновый цикл пула каждые
`HealthCheckIntervalSeconds`. `factory.Devices` каждый раз строит новую выборку
из пула, поэтому массив устройств получен один раз. Порядок обхода задан
сортировкой по `Id` (пул хранит устройства в `ConcurrentDictionary`).

**Предостережения.**

- Последовательный обход складывает задержки: десять недоступных устройств
  по 1 с таймаута дадут цикл длиннее периода. В таком случае опрашивайте
  устройства параллельно (`Task.WhenAll`), помня, что внутри одного устройства
  запросы всё равно сериализуются.
- `device.State` — состояние пула, а не результат последнего запроса; после
  первого успешного чтения оно станет `Connected`.
- Не создавайте `Mvk01DeviceFactory` на каждый тик: фабрика кэширует фасады,
  повторное создание — лишняя работа.
- Не забывайте `await using`/`DisposeAsync` пула: иначе фоновый цикл
  обслуживания и сокеты останутся жить после выхода.
- Устройства с `Enabled = false` вернут `DeviceDisabled` — это не сбой связи;
  исключайте их из обхода заранее.

---

## 10. Журнал состояний устройства

**Задача.** Писать в журнал все переходы состояний (`Disconnected`, `Connecting`,
`Connected`, `Faulted`) с ошибкой-причиной, не смешивая строки из разных потоков
и корректно отписываясь при завершении.

**Решение.** Подписка на `IMvkDevicePool.DeviceStateChanged` (или
`IMvkDevice.StateChanged` для одного устройства).

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Mvk.Modbus;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Pool;

internal static class Program
{
    private static readonly object ConsoleLock = new();

    private static async Task Main()
    {
        MvkModbusOptions options = new()
        {
            Pool = new MvkPoolOptions { ReconnectEnabled = true, HealthCheckIntervalSeconds = 5 },
            Devices =
            [
                new MvkDeviceOptions { Id = "mvk-1", Host = "10.20.0.211" },
                new MvkDeviceOptions { Id = "mvk-2", Host = "10.20.0.212" }
            ]
        };

        await using MvkDevicePool pool = new(options);
        pool.DeviceStateChanged += OnDeviceStateChanged;

        try
        {
            // Провоцируем подключения, чтобы увидеть переходы: Connected/Faulted.
            foreach (IMvkDevice device in pool.Devices)
            {
                try
                {
                    await device.EnsureConnectedAsync();
                }
                catch (MvkModbusException ex)
                {
                    WriteLine($"  {device.Settings.Id}: подключение не удалось — {ex.ErrorKind}");
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(30));   // наблюдаем за состояниями
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            pool.DeviceStateChanged -= OnDeviceStateChanged;
        }
    }

    private static void OnDeviceStateChanged(object? sender, MvkDeviceStateChangedEventArgs args)
    {
        string error = args.Error is null
            ? "без ошибки"
            : $"{args.Error.ErrorKind}: {args.Error.Message}";

        WriteLine($"{args.Device.Settings.Id}: {args.OldState} -> {args.NewState} ({error})");
    }

    private static void WriteLine(string message)
    {
        // Событие приходит из фоновых потоков пула — вывод защищаем блокировкой.
        lock (ConsoleLock)
        {
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  {message}");
        }
    }
}
```

Пример журнала:

```
14:31:02.114  mvk-2: Disconnected -> Connecting (без ошибки)
14:31:02.115  mvk-2: Connecting -> Faulted (Transport: Не удалось подключиться ...)
14:31:05.116  mvk-2: Faulted -> Connecting (без ошибки)
14:31:05.220  mvk-2: Connecting -> Connected (без ошибки)
14:31:03.002  mvk-1: Disconnected -> Connecting (без ошибки)
14:31:03.108  mvk-1: Connecting -> Connected (без ошибки)
```

**Пояснение.** Событие поднимается при каждом изменении состояния
(`MvkDevice.SetState`): при подключении, при временной ошибке запроса
(`Faulted` с ошибкой), при успешном реконнекте. `LastError` устройства
сбрасывается при первом успешном запросе. Для одного устройства вместо события
пула можно подписаться на `device.Device.StateChanged`.

**Предостережения.**

- **Обработчик вызывается синхронно.** `StateChanged` поднимается из
  `SetState`, в том числе внутри обработки запроса, когда слот запросов
  устройства занят. Не вызывайте методы устройства из обработчика напрямую —
  это может привести к взаимной блокировке; при необходимости запускайте
  работу через `Task.Run`.
- **Исключение из обработчика не глотается**: оно уйдёт в вызывающий код
  запроса. Оборачивайте тело обработчика в `try/catch`, если он может падать.
- Отписывайтесь (`-=`) перед освобождением пула — иначе держите ссылку на
  обработчик и получаете события после закрытия окна/выхода.
- `Connecting` может появляться повторно через `Faulted -> Connecting` —
  это нормальный цикл реконнекта, не считайте это «дребезгом».
- `TryGetDevice`, `Devices` и события потокобезопасны, но тяжёлая логика
  в обработчике замедляет фоновый цикл обслуживания пула.

---

## 11. Тахометр: частота, направление, переходы

**Задача.** Следить за тахометром 1: выводить частоту вращения, направление,
число переходов и вычислять период между опросами по разнице `TransitionCount`.

**Решение.** `ReadTachoAsync(1)` раз в секунду; разница циклического счётчика
переходов даёт обороты за интервал.

```csharp
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Mvk.MVK01;

internal static class Program
{
    private static async Task Main()
    {
        await using Mvk01Connection mvk = await Mvk01Connection.ConnectAsync("10.20.0.211");
        IMvk01Device device = mvk.Device;

        using CancellationTokenSource cts = new();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        uint previousTransitions = 0;
        long previousTimestamp = Stopwatch.GetTimestamp();
        bool hasPrevious = false;

        using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cts.Token))
            {
                Mvk01TachoMeasurement tacho = await device.ReadTachoAsync(1, cts.Token);

                long now = Stopwatch.GetTimestamp();
                double elapsed = Stopwatch.GetElapsedTime(previousTimestamp, now).TotalSeconds;

                if (hasPrevious)
                {
                    // Счётчик циклический (uint32), поэтому разность берём unchecked.
                    uint delta = unchecked(tacho.TransitionCount - previousTransitions);

                    if (delta == 0)
                    {
                        Console.WriteLine($"{DateTime.Now:HH:mm:ss}  вращение не обнаружено за {elapsed:F3} с");
                    }
                    else
                    {
                        double revolutionsPerSecond = delta / elapsed;
                        double periodPerRevolution = elapsed / delta;

                        Console.WriteLine($"{DateTime.Now:HH:mm:ss}  частота прибора {tacho.FrequencyHz:F3} Гц, "
                            + $"направление {DescribeDirection(tacho.Direction)}, "
                            + $"переходов +{delta} за {elapsed:F3} с -> {revolutionsPerSecond:F3} об/с, "
                            + $"период {periodPerRevolution * 1000:F2} мс");
                    }
                }

                Console.WriteLine($"  датчик: ошибка {tacho.SensorError}, уровень входа {tacho.InputLevel}, "
                    + $"метка перехода {tacho.TimestampSeconds:F6} с, всего переходов {tacho.TransitionCount}");

                previousTransitions = tacho.TransitionCount;
                previousTimestamp = now;
                hasPrevious = true;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static string DescribeDirection(Mvk01RotationDirection direction)
    {
        return direction switch
        {
            Mvk01RotationDirection.BelowMinimum => "ниже минимума",
            Mvk01RotationDirection.Tacho1ToTacho2 => "1 -> 2",
            Mvk01RotationDirection.Tacho2ToTacho1 => "2 -> 1",
            _ => $"неизвестно ({(uint)direction})"
        };
    }
}
```

**Пояснение.** `FrequencyHz` — частота, измеренная устройством (с учётом
гистерезиса); расчёт по переходам даёт независимую оценку: при одном импульсе
на оборот `об/с = Δпереходов / Δt`, а период оборота — `Δt / Δпереходов`.
`AverageIncrementHz` — среднее приращение частоты на оборот (полезно для
диагностики нестабильности), `TimestampSeconds` — метка последнего перехода
в секундах (значение регистра делится на 65536). Направление заполняется только
для тахометра 1 и только при энкодере на обоих входах; иначе будет
`BelowMinimum`.

**Предостережения.**

- `TransitionCount` — **циклический** `uint32`: вычитание делайте через
  `unchecked`, иначе после переполнения получите отрицательное значение.
- При перезапуске устройства счётчик начинается с нуля: сразу после рестарта
  разница может быть недостоверной. Проверяйте `device.ReadDeviceInfoAsync().Uptime`
  или сверяйте вычисленную частоту с `FrequencyHz`.
- Если частота вращения выше частоты опроса (пропуск переходов между тиками),
  расчёт по разнице всё ещё даёт средний темп, но период отдельного оборота —
  нет; для деталей читайте `TimestampSeconds` чаще.
- Остановка (`FrequencyHz ≈ 0`, `delta = 0`) — штатная ситуация, а не ошибка
  связи; `SensorError = true` означает другое: период меньше минимально
  допустимого.
- Длительность интервала измеряйте монотонными часами (`Stopwatch`), а не
  `DateTime.Now`: перевод часов или переход на летнее время исказит расчёт.

---

## 12. WPF: ViewModel устройства

**Задача.** Сделать WPF-представление: состояние соединения, последняя ошибка,
значение СКЗ, кнопки «Подключить»/«Отключить», обновление интерфейса через
`Dispatcher`.

**Решение.** MVVM: ViewModel не знает о контролах, все обновления свойств —
в UI-потоке; команды — асинхронные.

Проект:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\src\Mvk.MVK01\Mvk.MVK01.csproj" />
  </ItemGroup>
</Project>
```

`Mvk01ViewModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using Mvk.Modbus;
using Mvk.Modbus.Pool;
using Mvk.MVK01;

namespace Mvk.WpfSample;

/// <summary>
/// ViewModel одного устройства МВК01: подключение, отключение и опрос
/// СКЗ 10…1000 Гц по каналу 1. Свойства обновляются только в UI-потоке.
/// </summary>
public sealed class Mvk01ViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly Func<CancellationToken, Task<Mvk01Connection>> connectAsync;

    private Mvk01Connection? connection;
    private CancellationTokenSource? pollCts;
    private Task? pollTask;
    private string connectionState = MvkDeviceState.Disconnected.ToString();
    private string measuredValue = "—";
    private string lastError = "—";
    private bool isBusy;

    public Mvk01ViewModel(
        Dispatcher dispatcher,
        Func<CancellationToken, Task<Mvk01Connection>> connectAsync)
    {
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        this.connectAsync = connectAsync ?? throw new ArgumentNullException(nameof(connectAsync));

        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !isBusy && connection is null);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => !isBusy && connection is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand ConnectCommand { get; }

    public ICommand DisconnectCommand { get; }

    public string ConnectionState
    {
        get => connectionState;
        private set => Set(ref connectionState, value);
    }

    public string MeasuredValue
    {
        get => measuredValue;
        private set => Set(ref measuredValue, value);
    }

    public string LastError
    {
        get => lastError;
        private set => Set(ref lastError, value);
    }

    private bool IsBusy
    {
        get => isBusy;
        set
        {
            if (Set(ref isBusy, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private async Task ConnectAsync()
    {
        IsBusy = true;
        try
        {
            connection = await connectAsync(CancellationToken.None);
            connection.Device.Device.StateChanged += OnDeviceStateChanged;

            ConnectionState = connection.Device.State.ToString();
            LastError = "—";

            pollCts = new CancellationTokenSource();
            pollTask = PollAsync(connection.Device, pollCts.Token);
        }
        catch (MvkModbusException ex)
        {
            LastError = $"{ex.ErrorKind}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DisconnectAsync()
    {
        IsBusy = true;
        try
        {
            if (pollCts is not null)
            {
                await pollCts.CancelAsync();
                if (pollTask is not null)
                {
                    try
                    {
                        await pollTask;
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }

                pollCts.Dispose();
                pollCts = null;
                pollTask = null;
            }

            if (connection is not null)
            {
                connection.Device.Device.StateChanged -= OnDeviceStateChanged;
                Mvk01Connection closing = connection;
                connection = null;
                await closing.DisposeAsync();
            }

            MeasuredValue = "—";
            ConnectionState = MvkDeviceState.Disconnected.ToString();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }

    private async Task PollAsync(IMvk01Device device, CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(500));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    float[] values = await device.ReadMeasurementByNumberAsync(
                        Mvk01MeasurementNumber.Rms10To1000Acceleration, cancellationToken);

                    string formatted = $"{values[0]:F4} м/с2";
                    await dispatcher.InvokeAsync(() =>
                    {
                        MeasuredValue = formatted;
                        LastError = "—";
                    }).Task;
                }
                catch (MvkModbusException ex) when (ex.IsTransient)
                {
                    await dispatcher.InvokeAsync(() =>
                    {
                        MeasuredValue = "—";
                        LastError = $"{ex.ErrorKind}: {ex.Message}";
                    }).Task;
                }
                catch (MvkModbusException ex)
                {
                    await dispatcher.InvokeAsync(() =>
                    {
                        LastError = $"{ex.ErrorKind}: {ex.Message}";
                    }).Task;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnDeviceStateChanged(object? sender, MvkDeviceStateChangedEventArgs args)
    {
        // Событие приходит из фонового потока пула — переносим в UI-поток.
        dispatcher.InvokeAsync(() =>
        {
            ConnectionState = args.NewState.ToString();
            if (args.Error is not null)
            {
                LastError = $"{args.Error.ErrorKind}: {args.Error.Message}";
            }
        });
    }

    private void Notify([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Notify(propertyName);
        return true;
    }
}

/// <summary>Простая асинхронная команда без внешних пакетов.</summary>
internal sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> execute;
    private readonly Func<bool>? canExecute;
    private bool isRunning;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        this.execute = execute;
        this.canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter)
    {
        return !isRunning && (canExecute?.Invoke() ?? true);
    }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        isRunning = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await execute();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            // Исключение из async void уронило бы приложение; в реальном коде — в лог.
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            isRunning = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
```

`MainWindow.xaml`:

```xml
<Window x:Class="Mvk.WpfSample.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="МВК01 — мониторинг" Height="240" Width="480">
    <StackPanel Margin="16">
        <StackPanel Orientation="Horizontal">
            <Button Content="Подключить" Command="{Binding ConnectCommand}"
                    Padding="12,4" Margin="0,0,8,0" />
            <Button Content="Отключить" Command="{Binding DisconnectCommand}"
                    Padding="12,4" />
        </StackPanel>

        <TextBlock Margin="0,16,0,0"
                   Text="{Binding ConnectionState, StringFormat='Состояние: {0}'}" />
        <TextBlock Margin="0,4,0,0"
                   Text="{Binding MeasuredValue, StringFormat='СКЗ 10…1000 Гц (канал 1): {0}'}" />
        <TextBlock Margin="0,12,0,0" TextWrapping="Wrap" Foreground="DarkRed"
                   Text="{Binding LastError, StringFormat='Ошибка: {0}'}" />
    </StackPanel>
</Window>
```

`MainWindow.xaml.cs`:

```csharp
using System;
using System.Windows;
using Mvk.MVK01;

namespace Mvk.WpfSample;

public partial class MainWindow : Window
{
    private readonly Mvk01ViewModel viewModel;

    public MainWindow()
    {
        InitializeComponent();

        viewModel = new Mvk01ViewModel(
            Dispatcher,
            cancellationToken => Mvk01Connection.ConnectAsync(
                "10.20.0.211", cancellationToken: cancellationToken));

        DataContext = viewModel;
    }

    protected override async void OnClosed(EventArgs e)
    {
        await viewModel.DisposeAsync();
        base.OnClosed(e);
    }
}
```

**Пояснение.** Вся связь с устройством — в ViewModel: `ConnectAsync` создаёт
`Mvk01Connection`, подписывается на `StateChanged` и запускает цикл опроса;
`DisconnectAsync` останавливает цикл, отписывается и освобождает соединение.
Ошибки связи отображаются в `LastError`, но не завершают опрос: пул сам
восстанавливает соединение, а следующий удачный тик снова покажет значение.

**Предостережения.**

- Не блокируйте UI-поток (`await task.Result`, `Task.Wait()`) — используйте
  `async`/`await`; обновление свойств — только через `Dispatcher`.
- Не помещайте подключение и таймеры в code-behind: там допустим только
  `DataContext = viewModel` и освобождение при закрытии окна.
- Отменяйте и дожидайтесь цикла опроса перед закрытием, иначе получите
  исключения после выхода из приложения.
- Для приложения с несколькими окнами/устройствами зарегистрируйте
  `AddMvkModbus` + `AddMvk01Devices` в DI и внедряйте `IMvk01DeviceFactory`,
  а не создавайте пул в каждой ViewModel.
- `Application.Current.Dispatcher` (или `Dispatcher` окна) должен быть получен
  в UI-потоке; не создавайте ViewModel в фоновом `Task.Run`.

---

## 13. Диагностика устройства

**Задача.** Проверить доступность устройства, вывести версию ПО, системные
ошибки (биты), MD5 ПО DSP, наработку и индикаторы DSP по каналам.

**Решение.** `EnsureConnectedAsync` + `ReadDeviceInfoAsync` + расшифровка битов.

```csharp
using System;
using System.Threading.Tasks;
using Mvk.MVK01;

internal static class Program
{
    private static async Task Main()
    {
        await using Mvk01Connection mvk = await Mvk01Connection.ConnectAsync("10.20.0.211");
        IMvk01Device device = mvk.Device;

        Console.WriteLine($"Устройство  : {device.Id} ({device.Settings.Endpoint})");
        Console.WriteLine($"Состояние   : {device.State}, соединение: {device.Device.IsConnected}");

        Mvk01DeviceInfo info = await device.ReadDeviceInfoAsync();

        Console.WriteLine($"Серийный №  : {info.SerialNumber}");
        Console.WriteLine($"Версия ПО   : {info.FirmwareVersion}");
        Console.WriteLine($"Наработка   : {FormatUptime(info.Uptime)}");
        Console.WriteLine($"Карта памяти: {info.MemoryCardKilobytes} КБ");
        Console.WriteLine($"MD5 ПО DSP  : {info.DspMd5}");
        Console.WriteLine($"Агрегат 1   : {DescribeZone(info.Unit1State.Zone)} (0x{info.Unit1State.Raw:X8})");
        Console.WriteLine($"Агрегат 2   : {DescribeZone(info.Unit2State.Zone)} (0x{info.Unit2State.Raw:X8})");

        Console.WriteLine();
        Console.WriteLine($"Системные ошибки, регистр 0x1006 = 0x{info.Errors.Raw:X8}: "
            + (info.Errors.IsClean ? "ошибок нет" : "есть активные ошибки"));
        PrintFlag("приём данных от DSP      ", info.Errors.DspReceiveError);
        PrintFlag("работа с SD-картой       ", info.Errors.SdCardError);
        PrintFlag("отправка данных к DSP    ", info.Errors.DspSendError);
        PrintFlag("инициализация Modbus/TCP ", info.Errors.ModbusInitializationError);
        PrintFlag("CRC конфигурации         ", info.Errors.ConfigurationCrcError);

        Console.WriteLine();
        Console.WriteLine("Индикаторы DSP (раздел 4.1.5):");
        for (int channel = 1; channel <= Mvk01RegisterMap.MaxChannels; channel++)
        {
            PrintFlag($"перегрузка входа канала {channel}", info.DspIndicators.IsChannelOverloaded(channel));
        }

        Console.WriteLine($"  флаги конфигурации : 0x{info.DspIndicators.ConfigurationFlags:X8}");
        Console.WriteLine($"  счётчик расчётов   : {info.DspIndicators.CalculationCounter}");
        Console.WriteLine($"  последний запрос   : {device.Device.LastRequestAt:yyyy-MM-dd HH:mm:ss} UTC");
    }

    private static void PrintFlag(string title, bool value)
    {
        Console.WriteLine($"  {(value ? "ДА " : "нет")}  {title}");
    }

    private static string DescribeZone(Mvk01Zone zone)
    {
        return zone switch
        {
            Mvk01Zone.Green => "Green",
            Mvk01Zone.Yellow => "Yellow",
            Mvk01Zone.Red => "Red",
            Mvk01Zone.SensorFailure => "SensorFailure",
            Mvk01Zone.InvalidConfiguration => "InvalidConfiguration",
            _ => "Undefined"
        };
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        return $"{(int)uptime.TotalDays} д {uptime.Hours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}";
    }
}
```

Пример вывода:

```
Устройство  : 10.20.0.211 (10.20.0.211:502)
Состояние   : Connected, соединение: True
Серийный №  : 00004730
Версия ПО   : 1.7.20.0
Наработка   : 1 д 20:54:13
Карта памяти: 1024 КБ
MD5 ПО DSP  : 000102030405060708090a0b0c0d0e0f
...
Системные ошибки, регистр 0x1006 = 0x00000000: ошибок нет
  нет  приём данных от DSP
  ...
```

**Пояснение.** `ReadDeviceInfoAsync` делает три запроса: серийный номер + версия
+ ошибки + наработка + карта (0x1000, 12 регистров), MD5 + состояния агрегатов
(0x100C, 12 регистров), индикаторы DSP (0x1020, 6 регистров). Наработка — целые
секунды (`uint`, регистр 0x1008). MD5 — 16 байт по 0x100C, выводится в нижнем
регистре hex. Биты системных ошибок: 0 — приём от DSP, 1–3 — SD-карта,
4 — отправка к DSP, 5 — инициализация Modbus/TCP, 6 — CRC конфигурации.
Для быстрой проверки доступности без чтения достаточно `EnsureConnectedAsync()`
или `Test-NetConnection host -Port 502` (см. `AGENTS.md`).

**Предостережения.**

- `EnsureConnectedAsync` при недоступном устройстве бросает `MvkModbusException`
  с `Transport`/`Timeout` — обрабатывайте её, если это проверка, а не старт.
- `Errors.IsClean` — только про активные системные ошибки; зоны `Yellow`/`Red`
  и перегрузки каналов в битах не отражаются.
- `SdCardError` объединяет биты 1–3: отдельные коды детализируются только
  в описании формата; при ошибке проверяйте карту и питание.
- `FirmwareVersion` — упакованное значение; сравнение строк «1.10.0.0» и
  «1.7.20.0» лексикографически неверно, сравнивайте `Mvk01FirmwareVersion`
  покомпонентно.
- `MemoryCardKilobytes = 0` может означать и отсутствие карты, и ошибку чтения;
  сопоставляйте с битами ошибок.

---

## 14. Длительная недоступность

**Задача.** Пережить длительное отсутствие связи: отличать временные ошибки от
постоянных, не заваливать лог сообщениями, дождаться автоматического реконнекта
и возобновить опрос.

**Решение.** Классификация ошибок по `IsTransient`, ограничитель частоты логов
и журнал переходов состояний.

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Mvk.Modbus;
using Mvk.Modbus.Pool;
using Mvk.MVK01;

internal static class Program
{
    private static async Task Main()
    {
        await using Mvk01Connection mvk = await Mvk01Connection.ConnectAsync("10.20.0.211");
        IMvk01Device device = mvk.Device;

        using CancellationTokenSource cts = new();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        // Переходы состояний пишем по одному разу на переход — это дешёвый «журнал жизни».
        device.Device.StateChanged += OnStateChanged;

        try
        {
            await PollAsync(device, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            device.Device.StateChanged -= OnStateChanged;
        }
    }

    private static async Task PollAsync(IMvk01Device device, CancellationToken cancellationToken)
    {
        RateLimitedLog log = new(TimeSpan.FromMinutes(5));
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(5));

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                float[] values = await device.ReadMeasurementByNumberAsync(
                    Mvk01MeasurementNumber.Rms10To1000Acceleration, cancellationToken);

                Console.WriteLine($"{DateTime.Now:HH:mm:ss}  канал 1: {values[0]:F4} м/с2");
                log.Reset();
            }
            catch (MvkModbusException ex) when (ex.IsTransient)
            {
                // Transport / Timeout / Busy: реконнект уже идёт фоном (backoff от
                // ReconnectBaseDelayMs до ReconnectMaxDelayMs), повторять вручную не нужно.
                log.Write($"нет связи: {ex.ErrorKind} — {ex.Message}");
            }
            catch (MvkModbusException ex)
            {
                // Validation / DeviceError / DeviceDisabled / ...: повтор бессмысленен,
                // нужно исправлять запрос, настройки или конфигурацию устройства.
                log.Write($"постоянная ошибка: {ex.ErrorKind} — {ex.Message}");
            }
        }
    }

    private static void OnStateChanged(object? sender, MvkDeviceStateChangedEventArgs args)
    {
        string error = args.Error is null ? string.Empty : $" ({args.Error.ErrorKind})";
        Console.WriteLine($"{DateTime.Now:HH:mm:ss}  состояние {args.Device.Settings.Id}: "
            + $"{args.OldState} -> {args.NewState}{error}");
    }
}

/// <summary>Ограничитель частоты сообщений: не чаще одного раза в заданный интервал.</summary>
internal sealed class RateLimitedLog
{
    private readonly TimeSpan interval;
    private readonly object sync = new();
    private DateTimeOffset lastWrite = DateTimeOffset.MinValue;

    public RateLimitedLog(TimeSpan interval)
    {
        this.interval = interval;
    }

    public void Write(string message)
    {
        lock (sync)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (now - lastWrite < interval)
            {
                return;
            }

            lastWrite = now;
            Console.WriteLine($"{DateTime.Now:HH:mm:ss}  [повтор подавлен, не чаще {interval.TotalMinutes:F0} мин] {message}");
        }
    }

    public void Reset()
    {
        lock (sync)
        {
            lastWrite = DateTimeOffset.MinValue;
        }
    }
}
```

Пример журнала при обрыве связи:

```
14:40:01  канал 1: 1.2345 м/с2
14:40:06  состояние mvk-1: Connected -> Faulted (Transport)
14:40:06  [повтор подавлен, не чаще 5 мин] нет связи: Transport — ...
14:41:21  состояние mvk-1: Faulted -> Connected
14:41:21  канал 1: 1.2301 м/с2
```

**Пояснение.** Реконнект выполняет фоновый цикл пула: `Faulted`-устройство
восстанавливается не чаще, чем позволяет экспоненциальная задержка
(`ReconnectBaseDelayMs` = 500 мс, далее с ростом до `ReconnectMaxDelayMs` = 30 с),
проверка идёт каждые `HealthCheckIntervalSeconds` (по умолчанию 15 с). Из
приложения ничего дополнительно делать не нужно: достаточно не спамить запросами и
дождаться очередного тика. `RateLimitedLog` печатает первую ошибку сразу, а
повторы — не чаще интервала; после успешного чтения (`Reset`) следующая ошибка
снова выводится немедленно.

Классификация ошибок:

| Категория | `IsTransient` | Что делать |
|---|---|---|
| `Transport`, `Timeout`, `Busy` | да | ждать автореконнекта, повторить на следующем тике |
| `DeviceError` (ответ-исключение Modbus) | нет | проверить запрос и карту регистров |
| `Validation` | нет | исправить адрес/количество/политику записи |
| `DeviceDisabled` | нет | включить устройство в конфигурации |
| `DeviceNotFound`, `Configuration` | нет | исправить конфигурацию пула |
| `Canceled` | нет | это отмена вызывающим кодом, не сбой |
| `Unknown` | нет | разбираться по сообщению и `ModbusExceptionCode` |

**Предостережения.**

- Не вызывайте `EnsureConnectedAsync()` в цикле каждые 100 мс: это дублирует
  фоновый maintenance и создаёт лишние подключения; одного цикла опроса
  достаточно.
- У `RetryMiddleware` уже есть внутренние повторы временных ошибок
  (`MaxRetries` = 2 с backoff `RetryDelayMs`); внешний цикл повторов «сверху»
  умножает нагрузку на устройство.
- Ошибки `Timeout` на медленном канале — иногда следствие малого
  `ReadTimeoutMs`, а не обрыва; поднимите таймаут, прежде чем считать связь
  нестабильной.
- Обработчик `StateChanged` синхронный: не выполняйте в нём запросы к
  устройству (см. рецепт 10), иначе получите блокировку.
- При долгом отсутствии связи следите за размером журнала: интервал 5 минут
  лучше заменить на запись «первая и последняя ошибка за час», если система
  должна работать неделями.

---

## Подводные камни

- **Чётность адреса и количества.** Каждый запрос МВК01 должен иметь чётный
  адрес и чётное число регистров, иначе устройство ответит ошибкой Modbus
  `0x01`. В рецептах все адреса чётные (0x206C, 0x0104, 0x1014); при ручном
  расчёте смещений проверяйте это сами.
- **Смещения — не адреса.** `RmsSetOffset = 0x60` и подобные константы заданы
  относительно `ChannelData(N)`, `ChannelConfig(N)` или `TachoData(N)`; забудете
  базу — прочитаете чужой блок.
- **Порядок байт всегда из настроек.** `RegisterCodec` без `Settings.ByteOrder`
  (жёсткий `BitConverter`) — источник «случайных» значений; для ВДМ82 устройство
  может быть в `Mixed`.
- **Параметр 48 против 49.** `RmsX25kHzAcceleration` (A+0x5E, номер 48) —
  отдельное значение X…25 кГц; `RmsSets[0]` (A+0x60, номер 49) — полоса
  10…3000 Гц. Это разные величины с разными единицами применения.
- **N=48: адрес 0x42F0.** Пример `0x41F0` в `MVK01_registers.txt` ошибочен;
  формула `0x4000 + (N−1)×16` даёт `0x42F0`, что подтверждено на устройстве
  и тестом `Mvk01RegisterMapTests`.
- **Длины в байтах, адреса в регистрах.** «Единицы измерения 32 байта» — это
  16 регистров; «набор СКЗ из 3 значений» — это 6 регистров при чтении одного
  набора целиком.
- **Строки каналов — swap + cp1251.** Единицы измерения и серийники датчиков
  читаются `DecodeTextSwapped`, серийный номер устройства — обычным
  `DecodeText`. Перепутаете — получите «2с/м» вместо «м/с2».
- **Кодировка по умолчанию — cp1251.** `Mvk01Codec.DefaultTextEncoding` используется
  и в `Mvk01Device`, и в `Mvk01DeviceFactory`; при недоступном провайдере кодовых
  страниц происходит откат в Latin1.
- **Лимит 125 регистров на запрос.** Библиотека разбивает снимок канала на
  пять чтений; при собственных запросах больше 125 регистров устройство
  отклонит запрос.
- **`TransitionCount` циклический.** Разность считайте через `unchecked`;
  после перезапуска устройства счётчик сбрасывается — результат на первом
  тике недостоверен.
- **События 4.1.4 — не текущее состояние.** Биты 9–11 («был переход») не
  заменяют `Zone`; отдельной команды их сброса документация не описывает,
  храните предыдущую зону в приложении.
- **`SaveConfiguration` не мгновенна.** Статус опрашивается по 200 мс до 3 с;
  `Timeout` не означает неудачу — проверьте регистр 0x0002 повторно. На реальном
  устройстве Save/Reboot/Restore — только с разрешения.
- **`AllowConfigurationWrite` широкий.** Флаг открывает всю область
  0x0000–0x0FFF, а не один регистр; включайте его только на время записи,
  если это возможно.
- **Не пишите `+0x0E` и `0x0000`.** Резервный коэффициент пересчёта и
  конфигурация DSP помечены «не писать»; запись ломает масштабирование канала.
- **`ReadChannelAsync` — 5 запросов.** Не вызывайте его в цикле 100–500 мс;
  для быстрых значений используйте `ReadMeasurementByNumberAsync` или прямое
  чтение двух регистров.
- **Отмена — это `OperationCanceledException`.** «Стоп» по `CancellationToken`
  не приходит как `MvkModbusException`; не путайте с ошибкой связи.
- **`RetryMiddleware` уже повторяет.** Дополнительные ручные повторы временных
  ошибок умножают нагрузку; полагайтесь на `IsTransient` и автореконнект.
- **Состояние `Faulted` — не приговор.** Следующий успешный запрос или
  фоновый maintenance вернут `Connected` и сбросят `LastError`.
- **События состояния синхронные.** Обработчик `StateChanged` выполняется
  в потоке, который меняет состояние (иногда удерживая слот запросов):
  не вызывайте из него методы устройства и не бросайте исключений.
- **Устройство может быть отключено.** `Enabled = false` даёт
  `DeviceDisabled` на каждый запрос — фильтруйте такие устройства заранее.
- **WPF: только UI-поток.** Обновление `ObservableCollection`/свойств из
  фонового потока даёт исключения или «зависшие» привязки; используйте
  `Dispatcher.InvokeAsync`.
- **Освобождайте ресурсы.** `await using` для `Mvk01Connection`/`MvkDevicePool`
  и отписка от событий (`-=`) обязательны, иначе фоновые задачи и сокеты
  переживут приложение.
- **Парсинг подписей.** `FormatBand` и интерполяция строк зависят от культуры
  («1,25 кГц» в русской); для машинной обработки берите
  `Mvk01SpectrumBands.CenterFrequencies` и форматируйте с
  `CultureInfo.InvariantCulture`.

---

## См. также

- [Фасад МВК01](07b-mvk01-device-facade.md) — `IMvk01Device`, `Mvk01Connection`,
  снимки, команды и взаимодействие с пулом.
- [Пайплайн middleware](06-pipeline-middleware.md) — таймауты, повторы, защита
  записи, логирование и собственные обработчики запросов.
- [Тестирование](09-testing.md) — эмулятор Modbus-slave `MvkTestServer` и
  fake-транспорт: как воспроизвести любой из рецептов без железа.
- [README](../README.md) — обзор библиотеки, быстрый старт и настройка.
