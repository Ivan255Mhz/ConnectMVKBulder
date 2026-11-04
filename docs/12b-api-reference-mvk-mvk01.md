# 12b. Справочник API — пространство имён `Mvk.MVK01`

Глава содержит полный справочник публичного API сборки `Mvk.MVK01` (C#/.NET 8):
карту регистров, перечисления, кодеки, структуры значений, снимки, фасад устройства
и DI-расширение. Источник истины — исходники `src/Mvk.MVK01/**`; описания членов
приведены по XML-документации кода.

Все типы находятся в пространстве имён `Mvk.MVK01`, кроме
`Mvk01ServiceCollectionExtensions` — она объявлена в `Mvk.MVK01.DependencyInjection`.
Типы `MvkByteOrder`, `RegisterCodec`, `ModbusRequest`, `ModbusResponse`, `MvkModbusException`
принадлежат сборке `Mvk.Modbus` (см. [Справочник Mvk.Modbus](12a-api-reference-mvk-modbus.md)).

Содержание:

1. [Mvk01RegisterMap — карта регистров](#1-mvk01registermap--карта-регистров)
2. [Перечисления](#2-перечисления)
3. [Mvk01SpectrumBands — полосы спектра](#3-mvk01spectrumbands--полосы-спектра)
4. [Mvk01Codec — кодеки](#4-mvk01codec--кодеки)
5. [Mvk01Values — структуры значений](#5-mvk01values--структуры-значений)
6. [Mvk01Snapshots — снимки](#6-mvk01snapshots--снимки)
7. [Фасад устройства](#7-фасад-устройства)
8. [Mvk.MVK01.DependencyInjection](#8-mvkmvk01dependencyinjection)
9. [См. также](#см-также)

---

## 1. Mvk01RegisterMap — карта регистров

```csharp
namespace Mvk.MVK01;

public static class Mvk01RegisterMap
```

Карта регистров МВК01 (ПО 1.0.61+), адреса — в 16-битных регистрах Modbus.
Статический класс только с константами и чистыми функциями адресации; обращений
к устройству не выполняет.

Области адресного пространства:

| Диапазон | Область | Доступ |
|---|---|---|
| `0x0000–0x00FF` | Общие параметры устройства (раздел 3.1.1) | чтение/запись |
| `0x0100–0x08FF` | Конфигурация каналов АЦП, 8 блоков по `0x0100` (раздел 3.2.1) | чтение/запись |
| `0x0900–0x0AFF` | Конфигурация тахометров, 2 блока по `0x0100` (раздел 3.3.1) | чтение/запись |
| `0x1000–0x1FFF` | Системные параметры (раздел 4.1.1) | только чтение |
| `0x2000–0x2FFF` | Расчётные параметры каналов АЦП, 8 блоков по `0x0200` (раздел 5.1.1) | только чтение |
| `0x3000–0x31FF` | Расчётные параметры тахометров, 2 блока по `0x0100` (раздел 5.2.1) | только чтение |
| `0x4000+` | Параметры по номерам, шаг 16 (раздел 5.1.2) | только чтение |

### 1.1 Общие ограничения

| Сигнатура | Описание |
|---|---|
| `public const int MaxChannels = 8` | Максимальное число каналов АЦП |
| `public const int MaxTachoChannels = 2` | Максимальное число каналов тахометра |
| `public const int SpectrumBands = 43` | Число полос дольоктавного спектра |
| `public const int MaxMeasurementNumber = 184` | Максимальный номер расчётного параметра (раздел 5.1.2) |

### 1.2 Раздел 3.1.1 — общие параметры устройства (чтение/запись)

| Сигнатура | Описание |
|---|---|
| `public const ushort SystemCommand = 0x0002` | Системная команда/статус выполнения (uint32, раздел 3.1.2) |
| `public const ushort IpAddress = 0x0004` | IP-адрес устройства (4 байта) |
| `public const ushort SubnetMask = 0x0006` | Маска подсети (4 байта) |
| `public const ushort Gateway = 0x0008` | Адрес основного шлюза (4 байта) |
| `public const ushort ModbusPort = 0x000A` | Номер TCP-порта Modbus/TCP |
| `public const ushort ByteOrder = 0x000C` | Порядок передачи байт для Modbus-клиента (раздел 3.1.4) |
| `public const ushort SampleRate = 0x000E` | Частота дискретизации DSP (только чтение) |
| `public const ushort MaxRpmFrequency = 0x0010` | Максимальное значение частоты вращения (тахометр), Гц |
| `public const ushort MinRpmFrequency = 0x0012` | Минимальное значение частоты вращения (тахометр), Гц |
| `public const ushort ControlMode = 0x0014` | Режим контроля и диагностики (раздел 3.1.5) |
| `public const ushort ChannelsPerUnit = 0x0016` | Число каналов, относящихся к 1-му агрегату (1–7; 0 = по умолчанию 4) |
| `public const ushort ClientTimeout = 0x0018` | Таймаут отсутствия активности Modbus-клиента, с |
| `public const ushort SpectrumDataType = 0x001A` | Тип данных дольоктавного спектра (0-ускорение, 1-скорость, 2-перемещение) |
| `public const ushort AdcCalibration = 0x0020` | Калибровочные коэффициенты каналов АЦП (8 × 4 байта) |
| `public const ushort UartModuleType = 0x0040` | Тип дополнительного модуля UART (P7) |

### 1.3 Раздел 3.2.1 — конфигурация измерительных каналов АЦП (чтение/запись)

Базовые константы блока канала:

| Сигнатура | Описание |
|---|---|
| `public const ushort ChannelConfigBase = 0x0100` | Базовый адрес блока конфигурации первого канала |
| `public const ushort ChannelConfigBlockSize = 0x0100` | Размер блока конфигурации канала в регистрах |

Смещения относительно базы `ChannelConfig(n)`:

| Сигнатура | Описание |
|---|---|
| `public const ushort ChannelTypeOffset = 0x0000` | Тип канала (смещение) |
| `public const ushort ChannelSensorFactorOffset = 0x0002` | Коэффициент преобразования датчика, В/(м/с2) (смещение) |
| `public const ushort ChannelOverloadThresholdOffset = 0x0004` | Порог перегрузки в долях (смещение) |
| `public const ushort ChannelConversionFactorOffset = 0x000E` | Резерв: коэффициент пересчёта (смещение, не писать) |
| `public const ushort ChannelUnitsOffset = 0x0010` | Наименование единиц измеряемой величины (32 байта) |
| `public const ushort ChannelSerialOffset = 0x0020` | Серийный номер датчика (32 байта) |
| `public const ushort ChannelFilterX25kHzOffset = 0x003E` | Фильтр X…25 кГц — номер нижней частоты среза |
| `public const ushort ChannelFilter1LowOffset = 0x0040` | Фильтр 1 — номер нижней дольоктавы |
| `public const ushort ChannelFilter1HighOffset = 0x0042` | Фильтр 1 — номер верхней дольоктавы |
| `public const ushort ChannelFilter2LowOffset = 0x0044` | Фильтр 2 — номер нижней дольоктавы |
| `public const ushort ChannelFilter2HighOffset = 0x0046` | Фильтр 2 — номер верхней дольоктавы |
| `public const ushort ChannelMaxDcVoltageOffset = 0x0050` | Максимально допустимое постоянное напряжение на датчике, В |
| `public const ushort ChannelMinDcVoltageOffset = 0x0052` | Минимально допустимое постоянное напряжение на датчике, В |
| `public const ushort ChannelMainControlledParamOffset = 0x0060` | Конфигурация основного контролируемого параметра (32 байта) |
| `public const ushort ChannelControlledParamsOffset = 0x0070` | Конфигурация контролируемых параметров 2–8 (7 × 32 байта) |
| `public const ushort ControlledParamSize = 16` | Размер блока конфигурации контролируемого параметра в регистрах |

Метод адресации:

| Сигнатура | Описание |
|---|---|
| `public static ushort ChannelConfig(int channel)` | Адрес начала блока конфигурации канала АЦП; `channel` — 1–8 |

```csharp
ushort c1 = Mvk01RegisterMap.ChannelConfig(1);   // 0x0100
ushort c8 = Mvk01RegisterMap.ChannelConfig(8);   // 0x0800

// абсолютный адрес единиц измерения канала 1:
ushort units = (ushort)(Mvk01RegisterMap.ChannelConfig(1)
    + Mvk01RegisterMap.ChannelUnitsOffset);      // 0x0110
```

### 1.4 Раздел 3.3.1 — конфигурация каналов тахометра (чтение/запись)

| Сигнатура | Описание |
|---|---|
| `public const ushort TachoConfigBase = 0x0900` | Базовый адрес блока конфигурации первого тахометра |
| `public const ushort TachoConfigBlockSize = 0x0100` | Размер блока конфигурации тахометра в регистрах |
| `public const ushort TachoConfigHysteresisOffset = 0x0006` | Гистерезис (мин. время срабатывания), в 1/65536 долях секунды |
| `public const ushort TachoConfigAdditionalFunctionOffset = 0x0008` | Дополнительная функция (0-нет, 1-кнопка квитирования) |
| `public const ushort TachoConfigSerialOffset = 0x0020` | Серийный номер датчика тахометра (32 байта) |
| `public static ushort TachoConfig(int tacho)` | Адрес начала блока конфигурации тахометра; `tacho` — 1–2 |

```csharp
ushort t1 = Mvk01RegisterMap.TachoConfig(1);     // 0x0900
ushort t2 = Mvk01RegisterMap.TachoConfig(2);     // 0x0A00
```

### 1.5 Раздел 4.1.1 — системные параметры, общие (только чтение)

| Сигнатура | Описание |
|---|---|
| `public const ushort DeviceSerialNumber = 0x1000` | Серийный номер устройства (8 байт) |
| `public const ushort FirmwareVersion = 0x1004` | Версия внутреннего ПО (uint32, раздел 4.1.2) |
| `public const ushort SystemErrorIndicators = 0x1006` | Индикатор системных ошибок (uint32, раздел 4.1.3) |
| `public const ushort Uptime = 0x1008` | Счётчик времени работы, с |
| `public const ushort MemoryCardSize = 0x100A` | Размер установленной карты памяти, КБ |
| `public const ushort DspMd5 = 0x100C` | MD5-хэш внутреннего ПО DSP (16 байт) |
| `public const ushort Unit1ControlState = 0x1014` | Состояние контроля по агрегату 1 (раздел 4.1.4) |
| `public const ushort Unit2ControlState = 0x1016` | Состояние контроля по агрегату 2 (раздел 4.1.4) |
| `public const ushort DspIndicators = 0x1020` | Индикаторы работы DSP (3 × 4 байта, раздел 4.1.5) |

### 1.6 Раздел 5.1.1 — расчётные параметры каналов АЦП (только чтение)

Базовые константы блока канала:

| Сигнатура | Описание |
|---|---|
| `public const ushort ChannelDataBase = 0x2000` | Базовый адрес расчётных параметров первого канала |
| `public const ushort ChannelDataBlockSize = 0x0200` | Размер блока расчётных параметров канала в регистрах |

Смещения относительно базы `ChannelData(n)`:

| Сигнатура | Описание |
|---|---|
| `public const ushort SensorVoltageOffset = 0x0000` | Напряжение на датчике, В (смещение) |
| `public const ushort SpectrumOffset = 0x0002` | Дольоктавный спектр, 43 полосы × 2 регистра (смещение) |
| `public const ushort SpectrumRegisterCount = SpectrumBands * 2` | Число регистров дольоктавного спектра (86) |
| `public const ushort PeakFactorOffset = 0x005C` | Пик-фактор виброускорения X…25 кГц (смещение) |
| `public const ushort RmsX25kHzOffset = 0x005E` | СКЗ виброускорения X…25 кГц, м/с2 (смещение) |
| `public const ushort RmsSetOffset = 0x0060` | Наборы СКЗ (8 наборов × 3 значения), смещение |
| `public const int RmsSetCount = 8` | Число наборов СКЗ |
| `public const int RmsSetValues = 3` | Число значений в наборе СКЗ (ускорение, скорость, перемещение) |
| `public const ushort RmsSetRegisterCount = 48` | Число регистров всех наборов СКЗ |
| `public const ushort DcComponentOffset = 0x009A` | Постоянная составляющая в канале, м/с2 (смещение) |
| `public const ushort ChannelStateOffset = 0x009E` | Индикаторы состояния канала (uint32, раздел 5.1.4), смещение |
| `public const ushort EquivalentPeakOffset = 0x00A0` | Эквивалентные пиковые значения, 24 значения (смещение) |
| `public const int EquivalentValuesCount = 24` | Число эквивалентных значений (пиковых и размаха) |
| `public const ushort EquivalentRegisterCount = 48` | Число регистров эквивалентных значений |
| `public const ushort EquivalentRangeOffset = 0x00E0` | Эквивалентные значения размаха, 24 значения (смещение) |
| `public const ushort ChannelStateCopyOffset = 0x0120` | Копия индикаторов состояния канала (смещение) |
| `public const ushort ControlIndicatorsOffset = 0x0130` | Индикаторы контроля параметров 1–8 (8 × 8 регистров), смещение |
| `public const ushort ControlIndicatorsRegisterCount = 64` | Число регистров индикаторов контроля (8 параметров × 8 регистров) |
| `public const ushort ControlIndicatorSize = 8` | Размер блока индикаторов одного контролируемого параметра, регистры |

Метод адресации:

| Сигнатура | Описание |
|---|---|
| `public static ushort ChannelData(int channel)` | Адрес начала блока расчётных параметров канала АЦП; `channel` — 1–8 |

```csharp
ushort ch8 = Mvk01RegisterMap.ChannelData(8);    // 0x2000 + 7 × 0x200 = 0x2E00

// абсолютный адрес состояния канала 1:
ushort state = (ushort)(Mvk01RegisterMap.ChannelData(1)
    + Mvk01RegisterMap.ChannelStateOffset);      // 0x209E
```

### 1.7 Раздел 5.1.2 — расчётные параметры по номерам (только чтение)

| Сигнатура | Описание |
|---|---|
| `public const ushort MeasurementByNumberBase = 0x4000` | Базовый адрес блока параметров, адресация по номерам |
| `public const ushort MeasurementByNumberStride = 16` | Шаг между параметрами (8 каналов × 2 регистра) |
| `public const ushort MeasurementByNumberRegisterCount = 16` | Число регистров одного параметра (8 каналов × 2 регистра) |
| `public const int MeasurementByNumberChannels = 8` | Число каналов в блоке параметра |
| `public static ushort MeasurementByNumber(int number)` | Адрес параметра по его номеру (раздел 5.1.2); `number` — 1–184 |

```csharp
ushort p1 = Mvk01RegisterMap.MeasurementByNumber(1);     // 0x4000
ushort p48 = Mvk01RegisterMap.MeasurementByNumber(48);   // 0x42F0 (СКЗ X…25 кГц)
ushort p184 = Mvk01RegisterMap.MeasurementByNumber(184); // 0x4B70
```

### 1.8 Раздел 5.2.1 — расчётные параметры каналов тахометра (только чтение)

| Сигнатура | Описание |
|---|---|
| `public const ushort TachoDataBase = 0x3000` | Базовый адрес расчётных параметров первого тахометра |
| `public const ushort TachoDataBlockSize = 0x0100` | Размер блока расчётных параметров тахометра в регистрах |
| `public const ushort TachoFrequencyOffset = 0x0000` | Частота вращения, Гц (смещение) |
| `public const ushort TachoAverageIncrementOffset = 0x0002` | Среднее приращение частоты вращения на оборот, Гц (смещение) |
| `public const ushort TachoSensorErrorOffset = 0x0004` | Ошибка датчика оборотов (смещение) |
| `public const ushort TachoTimestampOffset = 0x0006` | Временная метка последнего перехода, 1/65536 с (смещение) |
| `public const ushort TachoInputLevelOffset = 0x0008` | Текущее значение уровня на входе тахометра (смещение) |
| `public const ushort TachoDirectionOffset = 0x000A` | Направление вращения (только для 1-го тахометра), смещение |
| `public const ushort TachoTransitionsOffset = 0x000C` | Число зарегистрированных переходов 0→1 (смещение) |
| `public const ushort TachoDataRegisterCount = 0x000E` | Число регистров блока расчётных данных тахометра |
| `public static ushort TachoData(int tacho)` | Адрес начала блока расчётных параметров тахометра; `tacho` — 1–2 |

```csharp
ushort td1 = Mvk01RegisterMap.TachoData(1);      // 0x3000
ushort td2 = Mvk01RegisterMap.TachoData(2);      // 0x3100
```

### 1.9 Гармоники частоты вращения

| Сигнатура | Описание |
|---|---|
| `public const ushort HarmonicsPhaseOffset = 0x7034` | Фаза 1–4 гармоник частоты вращения (абсолютное смещение от базы канала) |
| `public const ushort HarmonicsAmplitudeOffset = 0x703C` | Амплитуда 1–4 гармоник частоты вращения (абсолютное смещение от базы канала) |

Смещения абсолютные: адрес получается сложением базы канала со значением константы,
то есть фактически лежит в области `0x9000+`, а не внутри блока `0x2000–0x2FFF`.

```csharp
ushort phase = (ushort)(Mvk01RegisterMap.ChannelData(1)
    + Mvk01RegisterMap.HarmonicsPhaseOffset);    // 0x2000 + 0x7034 = 0x9034
```

### 1.10 Формулы-методы: сводка

| Метод | Формула | Пример |
|---|---|---|
| `ChannelConfig(n)` | `0x0100 + (n−1)×0x0100` | `ChannelConfig(1)` = 0x0100; `ChannelConfig(8)` = 0x0800 |
| `ChannelData(n)` | `0x2000 + (n−1)×0x0200` | `ChannelData(1)` = 0x2000; `ChannelData(8)` = 0x2E00 |
| `TachoConfig(n)` | `0x0900 + (n−1)×0x0100` | `TachoConfig(1)` = 0x0900; `TachoConfig(2)` = 0x0A00 |
| `TachoData(n)` | `0x3000 + (n−1)×0x0100` | `TachoData(1)` = 0x3000; `TachoData(2)` = 0x3100 |
| `MeasurementByNumber(N)` | `0x4000 + (N−1)×16` | `MeasurementByNumber(1)` = 0x4000; `MeasurementByNumber(48)` = 0x42F0 |

```csharp
using Mvk.MVK01;

// Адрес блока конфигурации канала 3:
ushort config = Mvk01RegisterMap.ChannelConfig(3);              // 0x0300

// Адрес начала наборов СКЗ канала 2:
ushort rms = (ushort)(Mvk01RegisterMap.ChannelData(2)
    + Mvk01RegisterMap.RmsSetOffset);                           // 0x2260

// Адрес параметра 80 (состояние канала) для всех каналов:
ushort state = Mvk01RegisterMap.MeasurementByNumber(80);        // 0x44F0
```

> Методы не валидируют аргументы: `ChannelConfig(0)`, `ChannelData(9)` или
> `MeasurementByNumber(200)` исключения не бросают, а возвращают адрес из чужого блока.
> Допустимые диапазоны: каналы 1–8, тахометры 1–2, номера 1–184.

---

## 2. Перечисления

Все перечисления объявлены в файлах `src/Mvk.MVK01/Mvk01Enums.cs` и
`src/Mvk.MVK01/Mvk01MeasurementNumber.cs`.

### 2.1 `Mvk01SystemCommand` (`: uint`)

Системные команды МВК01 (регистр 0x0002, раздел 3.1.2).

| Сигнатура | Значение | Описание |
|---|---|---|
| `RestoreConfiguration` | `0x10001` | Восстановить конфигурацию из Flash |
| `SaveConfiguration` | `0xF0001` | Сохранить текущую конфигурацию во Flash. Таймаут ответа увеличить до 3 с |
| `Reboot` | `0xA0001` | Перезапустить устройство |

### 2.2 `Mvk01CommandStatus` (`: int`)

Результат выполнения системной команды.

| Сигнатура | Значение | Описание |
|---|---|---|
| `Success` | `0x00000` | Операция успешно выполнена |
| `BadCommand` | `0x00BAD` | Клиент записал неверную команду |
| `FlashError` | `0xF0BAD` | Ошибка флэш-памяти, конфигурация не сохранена |
| `Pending` | `-1` | Команда принята, статус не определён (продолжается ожидание) |
| `Timeout` | `-2` | Истёк таймаут ожидания статуса |
| `TransportError` | `-3` | Ошибка транспорта при отправке команды или чтении статуса |

```csharp
Mvk01CommandResult result = await device.SaveConfigurationAsync();

if (result.Status == Mvk01CommandStatus.Success)
{
    Console.WriteLine("Конфигурация сохранена");
}
else if (result.Status == Mvk01CommandStatus.FlashError)
{
    Console.WriteLine("Ошибка Flash: конфигурация не сохранена");
}

// Отрицательные значения (-1..-3) — результат ожидания статуса библиотекой,
// а не содержимое регистра 0x0002. Сырое значение — в result.DeviceStatus.
```

### 2.3 `Mvk01Zone` (`: byte`)

Зона контроля агрегата (раздел 4.1.4, биты 0–7).

| Сигнатура | Значение | Описание |
|---|---|---|
| `Undefined` | `0x00` | Зона не определена |
| `Green` | `0x11` | Зелёная зона |
| `SensorFailure` | `0x12` | Отказ датчика |
| `InvalidConfiguration` | `0x13` | Неверная конфигурация |
| `Yellow` | `0x14` | Жёлтая зона |
| `Red` | `0x15` | Красная зона |

### 2.4 `Mvk01RotationDirection` (`: uint`)

Направление вращения (раздел 5.2.2, только для 1-го тахометра при энкодере).

| Сигнатура | Значение | Описание |
|---|---|---|
| `BelowMinimum` | `0x00` | Частота меньше минимально допустимой |
| `Tacho1ToTacho2` | `0x02` | Вращение от тахометра 1 к тахометру 2 |
| `Tacho2ToTacho1` | `0x03` | Вращение от тахометра 2 к тахометру 1 |

### 2.5 `Mvk01SpectrumDataType` (`: uint`)

Тип данных дольоктавного спектра (регистр 0x001A).

| Сигнатура | Значение | Описание |
|---|---|---|
| `Acceleration` | `0` | Ускорение |
| `Velocity` | `1` | Скорость |
| `Displacement` | `2` | Перемещение |

### 2.6 `Mvk01FilterX25kHz` (`: uint`)

Фильтр X…25 кГц — номер нижней частоты среза (смещение канала 0x3E).

| Сигнатура | Значение | Описание |
|---|---|---|
| `Off` | `0` | Выключен |
| `Hz6400` | `1` | 6.4 кГц |
| `Hz10000` | `2` | 10 кГц |
| `Hz15000` | `3` | 15 кГц |

### 2.7 `Mvk01UartModuleType` (`: uint`)

Тип дополнительного модуля UART (регистр 0x0040).

| Сигнатура | Значение | Описание |
|---|---|---|
| `None` | `0` | Нет |
| `Kpa1V` | `1` | КПА-1В |
| `Vdm82` | `3` | ВДМ82 |

### 2.8 `Mvk01MeasurementNumber` (`: int`)

Номера расчётных параметров МВК01 для адресации по номерам (раздел 5.1.2).
Перечислены основные параметры; остальные доступны по числовому номеру (1–184).
Полный список членов:

| Сигнатура | Значение | Описание |
|---|---|---|
| `SensorVoltage` | `1` | Напряжение на датчике, В |
| `SpectrumBand1` | `2` | Полоса дольоктавного спектра 1 (0.63 Гц) |
| `SpectrumBand43` | `44` | Полоса дольоктавного спектра 43 (10 кГц) |
| `PeakFactorAcceleration` | `47` | Пик-фактор виброускорения X…25 кГц |
| `RmsX25kHzAcceleration` | `48` | СКЗ виброускорения X…25 кГц, м/с2 |
| `Rms10To3000Acceleration` | `49` | СКЗ виброускорения 10…3000 Гц, м/с2 |
| `Rms10To3000Velocity` | `50` | СКЗ виброскорости 10…3000 Гц, мм/с |
| `Rms10To3000Displacement` | `51` | СКЗ виброперемещения 10…3000 Гц, мкм |
| `Rms10To2000Acceleration` | `52` | СКЗ виброускорения 10…2000 Гц, м/с2 |
| `Rms10To2000Velocity` | `53` | СКЗ виброскорости 10…2000 Гц, мм/с |
| `Rms10To2000Displacement` | `54` | СКЗ виброперемещения 10…2000 Гц, мкм |
| `Rms10To1000Acceleration` | `55` | СКЗ виброускорения 10…1000 Гц, м/с2 |
| `Rms10To1000Velocity` | `56` | СКЗ виброскорости 10…1000 Гц, мм/с |
| `Rms10To1000Displacement` | `57` | СКЗ виброперемещения 10…1000 Гц, мкм |
| `Rms2To1000Acceleration` | `58` | СКЗ виброускорения 2…1000 Гц, м/с2 |
| `Rms2To1000Velocity` | `59` | СКЗ виброскорости 2…1000 Гц, мм/с |
| `Rms2To1000Displacement` | `60` | СКЗ виброперемещения 2…1000 Гц, мкм |
| `Rms0_8To300Acceleration` | `61` | СКЗ виброускорения 0.8…300 Гц, м/с2 |
| `Rms0_8To300Velocity` | `62` | СКЗ виброскорости 0.8…300 Гц, мм/с |
| `Rms0_8To300Displacement` | `63` | СКЗ виброперемещения 0.8…300 Гц, мкм |
| `Rms0_8To150Acceleration` | `64` | СКЗ виброускорения 0.8…150 Гц, м/с2 |
| `Rms0_8To150Velocity` | `65` | СКЗ виброскорости 0.8…150 Гц, мм/с |
| `Rms0_8To150Displacement` | `66` | СКЗ виброперемещения 0.8…150 Гц, мкм |
| `RmsFilter1Acceleration` | `67` | СКЗ виброускорения по фильтру 1, м/с2 |
| `RmsFilter1Velocity` | `68` | СКЗ виброскорости по фильтру 1, мм/с |
| `RmsFilter1Displacement` | `69` | СКЗ виброперемещения по фильтру 1, мкм |
| `RmsFilter2Acceleration` | `70` | СКЗ виброускорения по фильтру 2, м/с2 |
| `RmsFilter2Velocity` | `71` | СКЗ виброскорости по фильтру 2, мм/с |
| `RmsFilter2Displacement` | `72` | СКЗ виброперемещения по фильтру 2, мкм |
| `DcComponent` | `78` | Постоянная составляющая в канале, м/с2 |
| `SensorVoltageCopy` | `79` | Напряжение на датчике (копия параметра 1), В |
| `ChannelState` | `80` | Индикаторы состояния канала (раздел 5.1.4) |
| `EquivalentPeakStart` | `81` | Начало блока эквивалентных пиковых значений (24 параметра: 81–104) |
| `EquivalentRangeStart` | `123` | Начало блока эквивалентных значений размаха (24 параметра: 123–136) |
| `ChannelStateCopy` | `145` | Индикаторы состояния канала (копия, параметр 145) |
| `ControlParameter1Value` | `153` | Значение контролируемого параметра 1 (параметры 153–184) |

```csharp
// Все 8 каналов одного параметра одним запросом:
float[] rms = await device.ReadMeasurementByNumberAsync(
    Mvk01MeasurementNumber.Rms10To1000Acceleration);   // номер 55

// Любой номер можно передать числом:
float[] values = await device.ReadMeasurementByNumberAsync(123);
```

Примечания:

- полосы 2–42 в перечислении не перечислены — используйте числовой номер
  (полоса `k` — это номер `k + 1`: полоса 1 → 2, полоса 43 → 44);
- диапазон номеров — 1–184, в `Mvk01MeasurementNumber` перечислены только основные.

---

## 3. Mvk01SpectrumBands — полосы спектра

```csharp
namespace Mvk.MVK01;

public static class Mvk01SpectrumBands
```

Полосы дольоктавного спектра МВК01: 43 полосы, средние частоты в герцах
(раздел 5.1.3 карты регистров).

| Сигнатура | Описание |
|---|---|
| `public static int Count { get; }` | Число полос спектра (43) |
| `public static ReadOnlySpan<double> CenterFrequencies { get; }` | Средние частоты полос (Гц) |
| `public static double GetCenterFrequency(int bandNumber)` | Возвращает среднюю частоту полосы; `bandNumber` — 1–43, вне диапазона — `ArgumentOutOfRangeException` |
| `public static string FormatBand(int bandNumber)` | Форматирует подпись полосы, например «10 Гц» |

Средние частоты по номерам полос:

| № | Гц | № | Гц | № | Гц | № | Гц |
|---|---|---|---|---|---|---|---|
| 1 | 0.63 | 12 | 8.0 | 23 | 100 | 34 | 1250 |
| 2 | 0.8 | 13 | 10.0 | 24 | 125 | 35 | 1600 |
| 3 | 1.0 | 14 | 12.5 | 25 | 160 | 36 | 2000 |
| 4 | 1.25 | 15 | 16.0 | 26 | 200 | 37 | 2500 |
| 5 | 1.6 | 16 | 20.0 | 27 | 250 | 38 | 3150 |
| 6 | 2.0 | 17 | 25.0 | 28 | 315 | 39 | 4000 |
| 7 | 2.5 | 18 | 31.5 | 29 | 400 | 40 | 5000 |
| 8 | 3.15 | 19 | 40.0 | 30 | 500 | 41 | 6300 |
| 9 | 4.0 | 20 | 50.0 | 31 | 630 | 42 | 8000 |
| 10 | 5.0 | 21 | 63.0 | 32 | 800 | 43 | 10000 |
| 11 | 6.3 | 22 | 80.0 | 33 | 1000 | | |

```csharp
using Mvk.MVK01;

int bands = Mvk01SpectrumBands.Count;                    // 43
double f1 = Mvk01SpectrumBands.GetCenterFrequency(1);    // 0.63
double f20 = Mvk01SpectrumBands.GetCenterFrequency(20);  // 50.0
double f43 = Mvk01SpectrumBands.GetCenterFrequency(43);  // 10000.0

string label = Mvk01SpectrumBands.FormatBand(43);        // "10 кГц"
string label20 = Mvk01SpectrumBands.FormatBand(20);      // "50 Гц"

// Полный перебор полос:
for (int band = 1; band <= Mvk01SpectrumBands.Count; band++)
{
    double frequency = Mvk01SpectrumBands.GetCenterFrequency(band);
    Console.WriteLine($"{band}: {Mvk01SpectrumBands.FormatBand(band)} ({frequency} Гц)");
}
```

Правило форматирования: при частоте `>= 1000` — «{частота / 1000} кГц», иначе
«{частота} Гц»; формат числа `0.###` использует текущую культуру.

---

## 4. Mvk01Codec — кодеки

```csharp
namespace Mvk.MVK01;

public static class Mvk01Codec
```

Декодирование значений МВК01 на основе `RegisterCodec` (пространство
`Mvk.Modbus.Protocol`).

### 4.1 Константы статусов

| Сигнатура | Описание |
|---|---|
| `public const uint StatusSuccess = 0x00000` | Статус: операция успешно выполнена |
| `public const uint StatusBadCommand = 0x00BAD` | Статус: клиент записал неверную команду |
| `public const uint StatusFlashError = 0xF0BAD` | Статус: ошибка флэш-памяти, конфигурация не сохранена |

### 4.2 Методы

| Сигнатура | Описание |
|---|---|
| `public static ushort[] EncodeSystemCommand(Mvk01SystemCommand command, MvkByteOrder byteOrder)` | Кодирует системную команду в два регистра |
| `public static Mvk01FirmwareVersion DecodeFirmwareVersion(uint packed)` | Декодирует версию ПО (раздел 4.1.2) |
| `public static Mvk01SystemErrors DecodeSystemErrors(uint raw)` | Декодирует индикатор системных ошибок (раздел 4.1.3) |
| `public static Mvk01UnitState DecodeUnitState(uint raw)` | Декодирует состояние контроля по агрегату (раздел 4.1.4) |
| `public static Mvk01ChannelState DecodeChannelState(uint raw)` | Декодирует индикаторы состояния канала (раздел 5.1.4) |
| `public static Mvk01DspIndicators DecodeDspIndicators(ReadOnlySpan<ushort> registers, MvkByteOrder byteOrder)` | Декодирует индикаторы работы DSP (раздел 4.1.5); требуется 6 регистров, иначе `ArgumentException` |
| `public static double DecodeTachoTimestampSeconds(uint raw)` | Декодирует метку времени тахометра (1/65536 с) в секунды |
| `public static float[] DecodeFloats(ReadOnlySpan<ushort> registers, int valueCount, MvkByteOrder byteOrder)` | Декодирует массив значений float из регистров (по два на значение); при нехватке — `ArgumentException` |
| `public static string DecodeText(ReadOnlySpan<ushort> registers, int byteCount, Encoding? encoding = null)` | Декодирует строку МВК01 из регистров (старший байт регистра — первый символ); по умолчанию `DefaultTextEncoding` |
| `public static string DecodeTextSwapped(ReadOnlySpan<ushort> registers, int byteCount, Encoding? encoding = null)` | Декодирует строку канала МВК01 (байты внутри каждого 4-байтного слова — в обратном порядке) |
| `public static ushort[] EncodeTextSwapped(string text, int registerCount, Encoding? encoding = null)` | Кодирует строку канала МВК01 в регистры (обратное к `DecodeTextSwapped`) |
| `public static string FormatMd5(ReadOnlySpan<byte> md5Bytes)` | Преобразует байты в строку MD5 (нижний регистр, без разделителей) |
| `public static Encoding DefaultTextEncoding { get; }` | Кодировка строк по умолчанию: кодовая страница 1251, при недоступности — Latin1 |

### 4.3 `EncodeSystemCommand`

Кодирует `Mvk01SystemCommand` в два регистра с учётом порядка байт устройства.

```csharp
ushort[] big = Mvk01Codec.EncodeSystemCommand(
    Mvk01SystemCommand.SaveConfiguration, MvkByteOrder.BigEndian);
// [0x000F, 0x0001]

ushort[] mixed = Mvk01Codec.EncodeSystemCommand(
    Mvk01SystemCommand.SaveConfiguration, MvkByteOrder.Mixed);
// [0x0001, 0x000F]
```

### 4.4 `DecodeFirmwareVersion`

Раскладка упакованного значения: биты 24–31 — версия, 16–23 — подверсия 1,
8–15 — подверсия 2, 0–7 — сборка.

```csharp
Mvk01FirmwareVersion version = Mvk01Codec.DecodeFirmwareVersion(0x01071400);
Console.WriteLine(version);   // 1.7.20.0
```

### 4.5 `DecodeSystemErrors`

| Бит | Поле | Смысл |
|---|---|---|
| 0 | `DspReceiveError` | Ошибка приёма данных от DSP |
| 1–3 | `SdCardError` | Ошибка работы с SD-картой (маска `0b1110`) |
| 4 | `DspSendError` | Ошибка отправки данных к DSP |
| 5 | `ModbusInitializationError` | Ошибка инициализации Modbus/TCP |
| 6 | `ConfigurationCrcError` | Ошибка CRC конфигурации |

```csharp
Mvk01SystemErrors errors = Mvk01Codec.DecodeSystemErrors(0x31);
// DspReceiveError = true, DspSendError = true, ModbusInitializationError = true
if (!errors.IsClean)
{
    Console.WriteLine($"Системные ошибки: 0x{errors.Raw:X8}");
}
```

### 4.6 `DecodeUnitState`

| Биты | Поле |
|---|---|
| 0–7 | `Zone` (`Mvk01Zone`) |
| 9 | `SensorFailureEvent` |
| 10 | `EnteredYellowZone` |
| 11 | `EnteredRedZone` |

```csharp
Mvk01UnitState state = Mvk01Codec.DecodeUnitState(0x14 | (1u << 10) | (1u << 11));
// Zone = Mvk01Zone.Yellow, EnteredYellowZone = true, EnteredRedZone = true
```

### 4.7 `DecodeChannelState`

| Бит | Поле | Смысл |
|---|---|---|
| 0 | `Overload` | Перегрузка измерительного входа |
| 4 | `Buffer16HzNotFilled` | Буфер 16 Гц не заполнен |
| 5 | `Buffer64HzNotFilled` | Буфер 64 Гц не заполнен |
| 6 | `Buffer512HzNotFilled` | Буфер 512 Гц не заполнен |
| 10 | `DcVoltageAboveMaximum` | Постоянное напряжение выше нормы |
| 11 | `DcVoltageBelowMinimum` | Постоянное напряжение ниже нормы |

```csharp
Mvk01ChannelState state = Mvk01Codec.DecodeChannelState(1u | (1u << 6) | (1u << 11));
// Overload = true, Buffer512HzNotFilled = true, DcVoltageBelowMinimum = true
```

### 4.8 `DecodeDspIndicators`

Принимает не менее 6 регистров (`0x1020–0x1025`); при меньшем числе — `ArgumentException`,
лишние регистры игнорируются. Первая пара регистров — маска перегрузки, вторая — флаги
конфигурации, третья — счётчик процедур расчёта.

```csharp
Mvk01DspIndicators indicators = Mvk01Codec.DecodeDspIndicators(
    [0x0000, 0x0003, 0x0000, 0x0004, 0x0000, 0x002A],
    MvkByteOrder.BigEndian);

indicators.OverloadChannelMask;        // 0x03
indicators.IsChannelOverloaded(1);      // true
indicators.IsChannelOverloaded(3);      // false
indicators.ConfigurationFlags;         // 4
indicators.CalculationCounter;         // 42
```

### 4.9 `DecodeTachoTimestampSeconds`

Метка времени — uint32 в 1/65536 с; метод делит значение на 65536.

```csharp
double seconds = Mvk01Codec.DecodeTachoTimestampSeconds(65536);   // 1.0
double half = Mvk01Codec.DecodeTachoTimestampSeconds(32768);      // 0.5
```

### 4.10 `DecodeFloats`

Декодирует `valueCount` значений float из регистров (по два регистра на значение).

```csharp
float[] values = Mvk01Codec.DecodeFloats(
    [0x4145, 0x70A4, 0x0000, 0x0000],
    2,
    MvkByteOrder.BigEndian);
// values[0] ≈ 12.34, values[1] = 0
```

### 4.11 Строки: `DecodeText`, `DecodeTextSwapped`, `EncodeTextSwapped`

`DecodeText` — прямой порядок (старший байт регистра — первый символ); применяется
для серийного номера устройства. `DecodeTextSwapped` обрабатывает строки каналов
и тахометров: внутри каждого 4-байтного слова порядок байт обратный; строка обрезается
по первому нулевому байту и тримится.

```csharp
// Серийный номер устройства (прямой порядок), 8 байт = 4 регистра:
ushort[] serialRegisters = await device.ReadHoldingRegistersAsync(
    Mvk01RegisterMap.DeviceSerialNumber, 4, ct);
string serial = Mvk01Codec.DecodeText(serialRegisters, 8);

// Единицы измерения канала (swap внутри 4-байтных слов):
ushort[] unitsRegisters = [0x32F1, 0x2FEC, 0x0000, 0x0000];
string units = Mvk01Codec.DecodeTextSwapped(unitsRegisters, 8);   // "м/с2"

// Обратное преобразование для записи:
ushort[] back = Mvk01Codec.EncodeTextSwapped("м/с2", 4);
// [0x32F1, 0x2FEC, 0x0000, 0x0000]
```

Исключения `EncodeTextSwapped`: `ArgumentNullException` при `text == null`,
`ArgumentOutOfRangeException` при `registerCount <= 0`, `ArgumentException`, если
закодированная строка не помещается в заданное число регистров.

### 4.12 `FormatMd5`

```csharp
byte[] hash = [0x00, 0x11, 0xAB, 0xCD];
string md5 = Mvk01Codec.FormatMd5(hash);   // "0011abcd"
```

### 4.13 `DefaultTextEncoding`

Кодировка строк по умолчанию — cp1251 (провайдер кодовых страниц регистрируется
автоматически); если получить её не удалось, используется `Encoding.Latin1`.
Значение инициализируется один раз при инициализации типа.

---

## 5. Mvk01Values — структуры значений

Все типы объявлены в `src/Mvk.MVK01/Mvk01Values.cs`.

### 5.1 `Mvk01FirmwareVersion` (`readonly record struct`)

```csharp
public readonly record struct Mvk01FirmwareVersion(int Major, int Minor, int Patch, int Build)
```

Версия внутреннего ПО МВК01 (раздел 4.1.2): версия.подверсия1.подверсия2.сборка.

| Сигнатура | Описание |
|---|---|
| `public int Major { get; init; }` | Версия |
| `public int Minor { get; init; }` | Подверсия 1 |
| `public int Patch { get; init; }` | Подверсия 2 |
| `public int Build { get; init; }` | Сборка |
| `public override string ToString()` | Строка вида `«1.7.20.0»` |

```csharp
Mvk01FirmwareVersion version = Mvk01Codec.DecodeFirmwareVersion(0x01071400);
Console.WriteLine($"{version.Major}.{version.Minor}");   // 1.7
Console.WriteLine(version);                              // 1.7.20.0
```

### 5.2 `Mvk01SystemErrors` (`readonly record struct`)

```csharp
public readonly record struct Mvk01SystemErrors(
    bool DspReceiveError,
    bool SdCardError,
    bool DspSendError,
    bool ModbusInitializationError,
    bool ConfigurationCrcError,
    uint Raw)
```

Индикатор системных ошибок МВК01 (раздел 4.1.3).

| Сигнатура | Описание |
|---|---|
| `public bool DspReceiveError { get; init; }` | Ошибка приёма данных от DSP (бит 0) |
| `public bool SdCardError { get; init; }` | Ошибка SD-карты (биты 1–3) |
| `public bool DspSendError { get; init; }` | Ошибка отправки данных к DSP (бит 4) |
| `public bool ModbusInitializationError { get; init; }` | Ошибка инициализации Modbus/TCP (бит 5) |
| `public bool ConfigurationCrcError { get; init; }` | Ошибка CRC конфигурации (бит 6) |
| `public uint Raw { get; init; }` | Исходное значение целиком |
| `public bool IsClean { get; }` | Ошибок нет: `Raw == 0` |

### 5.3 `Mvk01UnitState` (`readonly record struct`)

```csharp
public readonly record struct Mvk01UnitState(
    Mvk01Zone Zone,
    bool SensorFailureEvent,
    bool EnteredYellowZone,
    bool EnteredRedZone,
    uint Raw)
```

Состояние контроля по агрегату (раздел 4.1.4).

| Сигнатура | Описание |
|---|---|
| `public Mvk01Zone Zone { get; init; }` | Зона контроля (биты 0–7) |
| `public bool SensorFailureEvent { get; init; }` | Событие отказа датчика (бит 9) |
| `public bool EnteredYellowZone { get; init; }` | Переход в жёлтую зону (бит 10) |
| `public bool EnteredRedZone { get; init; }` | Переход в красную зону (бит 11) |
| `public uint Raw { get; init; }` | Исходное значение целиком |

### 5.4 `Mvk01DspIndicators` (`readonly record struct`)

```csharp
public readonly record struct Mvk01DspIndicators(
    byte OverloadChannelMask,
    uint ConfigurationFlags,
    uint CalculationCounter)
```

Индикаторы работы DSP (раздел 4.1.5).

| Сигнатура | Описание |
|---|---|
| `public byte OverloadChannelMask { get; init; }` | Биты 0–7 — перегрузка каналов 1–8 |
| `public uint ConfigurationFlags { get; init; }` | Флаги ошибок конфигурации |
| `public uint CalculationCounter { get; init; }` | Счётчик выполненных процедур расчёта |
| `public bool IsChannelOverloaded(int channel)` | Перегрузка измерительного входа канала (1–8); вне диапазона — `ArgumentOutOfRangeException` |

```csharp
Mvk01DspIndicators indicators = Mvk01Codec.DecodeDspIndicators(registers, order);

for (int channel = 1; channel <= Mvk01RegisterMap.MaxChannels; channel++)
{
    if (indicators.IsChannelOverloaded(channel))
    {
        Console.WriteLine($"Перегрузка канала {channel}");
    }
}
```

### 5.5 `Mvk01ChannelState` (`readonly record struct`)

```csharp
public readonly record struct Mvk01ChannelState(
    bool Overload,
    bool Buffer16HzNotFilled,
    bool Buffer64HzNotFilled,
    bool Buffer512HzNotFilled,
    bool DcVoltageAboveMaximum,
    bool DcVoltageBelowMinimum,
    uint Raw)
```

Индикаторы состояния канала АЦП (раздел 5.1.4).

| Сигнатура | Описание |
|---|---|
| `public bool Overload { get; init; }` | Перегрузка измерительного входа (бит 0) |
| `public bool Buffer16HzNotFilled { get; init; }` | Буфер 16 Гц не заполнен (бит 4) |
| `public bool Buffer64HzNotFilled { get; init; }` | Буфер 64 Гц не заполнен (бит 5) |
| `public bool Buffer512HzNotFilled { get; init; }` | Буфер 512 Гц не заполнен (бит 6) |
| `public bool DcVoltageAboveMaximum { get; init; }` | Постоянное напряжение выше нормы (бит 10) |
| `public bool DcVoltageBelowMinimum { get; init; }` | Постоянное напряжение ниже нормы (бит 11) |
| `public uint Raw { get; init; }` | Исходное значение целиком |

### 5.6 `Mvk01RmsSet` (`sealed record`)

```csharp
public sealed record Mvk01RmsSet(
    string Name,
    float Acceleration,
    float Velocity,
    float Displacement)
```

Набор СКЗ канала: ускорение, скорость, перемещение в одной полосе частот.

| Сигнатура | Описание |
|---|---|
| `public string Name { get; init; }` | Название полосы (например, «10…3000 Гц») |
| `public float Acceleration { get; init; }` | СКЗ виброускорения, м/с2 |
| `public float Velocity { get; init; }` | СКЗ виброскорости, мм/с |
| `public float Displacement { get; init; }` | СКЗ виброперемещения, мкм |

```csharp
Mvk01RmsSet set = channel.RmsSets[2];    // «10…1000 Гц»
Console.WriteLine($"{set.Name}: {set.Acceleration:F3} м/с2");
```

---

## 6. Mvk01Snapshots — снимки

Все типы объявлены в `src/Mvk.MVK01/Mvk01Snapshots.cs`. Свойства объявлены как
`required ... { get; init; }` — объекты создаются инициализатором, сравнение —
по значению (record).

### 6.1 `Mvk01DeviceInfo` (`sealed record`)

Снимок общих сведений об устройстве (раздел 4.1.1).

| Сигнатура | Описание |
|---|---|
| `public required string SerialNumber { get; init; }` | Серийный номер устройства |
| `public required Mvk01FirmwareVersion FirmwareVersion { get; init; }` | Версия внутреннего ПО |
| `public required Mvk01SystemErrors Errors { get; init; }` | Индикатор системных ошибок |
| `public required TimeSpan Uptime { get; init; }` | Счётчик времени работы |
| `public required int MemoryCardKilobytes { get; init; }` | Размер установленной карты памяти, КБ |
| `public required string DspMd5 { get; init; }` | MD5-хэш внутреннего ПО DSP (шестнадцатеричная строка) |
| `public required Mvk01UnitState Unit1State { get; init; }` | Состояние контроля по агрегату 1 |
| `public required Mvk01UnitState Unit2State { get; init; }` | Состояние контроля по агрегату 2 |
| `public required Mvk01DspIndicators DspIndicators { get; init; }` | Индикаторы работы DSP |

```csharp
Mvk01DeviceInfo info = await device.ReadDeviceInfoAsync(ct);
Console.WriteLine($"Серийный: {info.SerialNumber}, ПО: {info.FirmwareVersion}");
Console.WriteLine($"Наработка: {info.Uptime}, SD: {info.MemoryCardKilobytes} КБ");
Console.WriteLine($"MD5 DSP: {info.DspMd5}, зона агрегата 1: {info.Unit1State.Zone}");

if (!info.Errors.IsClean)
{
    Console.WriteLine($"Системные ошибки: 0x{info.Errors.Raw:X8}");
}
```

### 6.2 `Mvk01Channel` (`sealed record`)

Снимок расчётных параметров канала АЦП (раздел 5.1.1).

| Сигнатура | Описание |
|---|---|
| `public required int Channel { get; init; }` | Номер канала (1–8) |
| `public required float SensorVoltage { get; init; }` | Напряжение на датчике, В |
| `public required float[] Spectrum { get; init; }` | Дольоктавный спектр, 43 полосы (единицы по типу спектра устройства) |
| `public required float PeakFactor { get; init; }` | Пик-фактор виброускорения X…25 кГц |
| `public required float RmsX25kHzAcceleration { get; init; }` | СКЗ виброускорения X…25 кГц, м/с2 |
| `public required IReadOnlyList<Mvk01RmsSet> RmsSets { get; init; }` | Наборы СКЗ по полосам частот |
| `public required float DcComponent { get; init; }` | Постоянная составляющая в канале, м/с2 |
| `public required Mvk01ChannelState State { get; init; }` | Индикаторы состояния канала |
| `public required float[] EquivalentPeakValues { get; init; }` | Эквивалентные пиковые значения (24 значения, соответствуют параметрам 49–72) |
| `public required float[] EquivalentRangeValues { get; init; }` | Эквивалентные значения размаха (24 значения, соответствуют параметрам 49–72) |
| `public float GetSpectrumBand(int bandNumber)` | Значение полосы спектра; `bandNumber` — 1–43, вне диапазона — `ArgumentOutOfRangeException` |

Порядок наборов `RmsSets` (индекс — полоса):

| Индекс | `Name` | Индекс | `Name` |
|---|---|---|---|
| 0 | «10…3000 Гц» | 4 | «0.8…300 Гц» |
| 1 | «10…2000 Гц» | 5 | «0.8…150 Гц» |
| 2 | «10…1000 Гц» | 6 | «Фильтр 1» |
| 3 | «2…1000 Гц» | 7 | «Фильтр 2» |

> В XML-документации исходного кода для обоих массивов указано «соответствуют
> параметрам 49–72»; в перечислении `Mvk01MeasurementNumber` начала блоков заданы
> иначе — `EquivalentPeakStart = 81` (81–104) и `EquivalentRangeStart = 123` (123–136).

```csharp
Mvk01Channel channel = await device.ReadChannelAsync(1, ct);

Console.WriteLine($"Напряжение: {channel.SensorVoltage:F4} В");
Console.WriteLine($"СКЗ X…25 кГц: {channel.RmsX25kHzAcceleration:F3} м/с2");
Console.WriteLine($"10…1000 Гц: {channel.RmsSets[2].Acceleration:F3} м/с2");

float band20 = channel.GetSpectrumBand(20);             // полоса 50 Гц
float band43 = channel.GetSpectrumBand(43);             // полоса 10 кГц

if (channel.State.Overload)
{
    Console.WriteLine("Перегрузка измерительного входа");
}
```

### 6.3 `Mvk01TachoMeasurement` (`sealed record`)

Снимок расчётных параметров канала тахометра (раздел 5.2.1).

| Сигнатура | Описание |
|---|---|
| `public required int Tacho { get; init; }` | Номер тахометра (1–2) |
| `public required float FrequencyHz { get; init; }` | Частота вращения, Гц |
| `public required float AverageIncrementHz { get; init; }` | Среднее приращение частоты вращения на один оборот, Гц |
| `public required bool SensorError { get; init; }` | Ошибка датчика оборотов (период меньше минимально допустимого) |
| `public required double TimestampSeconds { get; init; }` | Временная метка последнего перехода (с) |
| `public required bool InputLevel { get; init; }` | Текущее значение уровня на входе тахометра |
| `public required Mvk01RotationDirection Direction { get; init; }` | Направление вращения (только для 1-го тахометра при энкодере) |
| `public required uint TransitionCount { get; init; }` | Число зарегистрированных переходов 0→1 |

```csharp
Mvk01TachoMeasurement tacho = await device.ReadTachoAsync(1, ct);
Console.WriteLine($"Частота: {tacho.FrequencyHz:F2} Гц, " +
                  $"направление: {tacho.Direction}, переходов: {tacho.TransitionCount}");

if (tacho.SensorError)
{
    Console.WriteLine("Ошибка датчика оборотов");
}
```

### 6.4 `Mvk01ChannelConfig` (`sealed record`)

Снимок конфигурации измерительного канала (раздел 3.2.1).

| Сигнатура | Описание |
|---|---|
| `public required int Channel { get; init; }` | Номер канала (1–8) |
| `public required int ChannelType { get; init; }` | Тип канала (по документации = 0) |
| `public required float SensorFactor { get; init; }` | Коэффициент преобразования датчика, В/(м/с2) |
| `public required float OverloadThreshold { get; init; }` | Порог перегрузки в долях |
| `public required string Units { get; init; }` | Наименование единиц измеряемой величины |
| `public required string SerialNumber { get; init; }` | Серийный номер датчика |
| `public required Mvk01FilterX25kHz FilterX25kHz { get; init; }` | Фильтр X…25 кГц — нижняя частота среза |
| `public required int Filter1LowBand { get; init; }` | Фильтр 1 — номер нижней дольоктавы |
| `public required int Filter1HighBand { get; init; }` | Фильтр 1 — номер верхней дольоктавы |
| `public required int Filter2LowBand { get; init; }` | Фильтр 2 — номер нижней дольоктавы |
| `public required int Filter2HighBand { get; init; }` | Фильтр 2 — номер верхней дольоктавы |
| `public required float MaxDcVoltage { get; init; }` | Максимально допустимое постоянное напряжение на датчике, В |
| `public required float MinDcVoltage { get; init; }` | Минимально допустимое постоянное напряжение на датчике, В |

```csharp
Mvk01ChannelConfig config = await device.ReadChannelConfigAsync(1, ct);
Console.WriteLine($"Датчик: {config.SerialNumber}, единицы: {config.Units}");
Console.WriteLine($"Коэффициент: {config.SensorFactor:F6} В/(м/с2)");
Console.WriteLine($"Фильтр X…25 кГц: {config.FilterX25kHz}, " +
                  $"фильтр 1: {config.Filter1LowBand}…{config.Filter1HighBand}");
```

### 6.5 `Mvk01CommandResult` (`sealed record`)

Результат выполнения системной команды.

| Сигнатура | Описание |
|---|---|
| `public required Mvk01SystemCommand Command { get; init; }` | Отправленная команда |
| `public required Mvk01CommandStatus Status { get; init; }` | Итоговый статус |
| `public uint? DeviceStatus { get; init; }` | Статус, прочитанный из устройства (если получен) |
| `public MvkModbusException? Error { get; init; }` | Ошибка транспорта (если была) |
| `public bool IsSuccess { get; }` | Команда выполнена успешно: `Status == Mvk01CommandStatus.Success` |
| `public override string ToString()` | `«Команда {Command}: {Status}»` или `«Команда {Command}: {Status} ({сообщение ошибки})»` |

```csharp
Mvk01CommandResult result = await device.RebootAsync(ct);

Console.WriteLine(result);                       // Команда Reboot: Success
if (!result.IsSuccess)
{
    Console.WriteLine($"Статус: {result.Status}, " +
                      $"устройство: 0x{result.DeviceStatus:X5}, " +
                      $"ошибка: {result.Error?.Message ?? "нет"}");
}
```

---

## 7. Фасад устройства

Типы объявлены в `src/Mvk.MVK01/IMvk01Device.cs`, `Mvk01Device.cs`, `Mvk01Connection.cs`,
`IMvk01DeviceFactory.cs`, `Mvk01DeviceFactory.cs`.

### 7.1 `IMvk01Device`

```csharp
namespace Mvk.MVK01;

public interface IMvk01Device
```

Типизированный фасад устройства МВК01: декодированные значения каналов, тахометров,
сведений об устройстве и системные команды.

Свойства:

| Сигнатура | Описание |
|---|---|
| `string Id { get; }` | Идентификатор устройства из конфигурации |
| `MvkDeviceSettings Settings { get; }` | Параметры устройства (с применёнными настройками по умолчанию) |
| `MvkDeviceState State { get; }` | Текущее состояние соединения |
| `IMvkDevice Device { get; }` | Базовое устройство пула (сырые регистры, события состояния) |

Методы:

| Сигнатура | Описание |
|---|---|
| `Task EnsureConnectedAsync(CancellationToken cancellationToken = default)` | Устанавливает соединение явно |
| `Task DisconnectAsync(CancellationToken cancellationToken = default)` | Закрывает соединение |
| `Task<Mvk01DeviceInfo> ReadDeviceInfoAsync(CancellationToken cancellationToken = default)` | Читает общие сведения: серийный номер, версию ПО, ошибки, наработку |
| `Task<Mvk01Channel> ReadChannelAsync(int channel, CancellationToken cancellationToken = default)` | Читает полный снимок канала АЦП: напряжение, спектр (43 полосы), СКЗ, пик-фактор, эквивалентные значения и состояния; `channel` — 1–8 |
| `Task<Mvk01ChannelConfig> ReadChannelConfigAsync(int channel, CancellationToken cancellationToken = default)` | Читает конфигурацию измерительного канала; `channel` — 1–8 |
| `Task<Mvk01TachoMeasurement> ReadTachoAsync(int tacho, CancellationToken cancellationToken = default)` | Читает снимок канала тахометра; `tacho` — 1–2 |
| `Task<float[]> ReadMeasurementByNumberAsync(Mvk01MeasurementNumber number, CancellationToken cancellationToken = default)` | Читает значения параметра по номеру для всех 8 каналов одним запросом (раздел 5.1.2, только ПО 1.0.61+) |
| `Task<float[]> ReadMeasurementByNumberAsync(int number, CancellationToken cancellationToken = default)` | Читает значения параметра по числовому номеру для всех 8 каналов одним запросом; `number` — 1–184 |
| `Task<ushort[]> ReadMeasurementRawByNumberAsync(int number, CancellationToken cancellationToken = default)` | Читает регистры параметра по номеру без декодирования (для битовых полей); `number` — 1–184 |
| `Task<Mvk01CommandResult> ExecuteSystemCommandAsync(Mvk01SystemCommand command, TimeSpan? timeout = null, CancellationToken cancellationToken = default)` | Выполняет системную команду с ожиданием статуса; `timeout` — общий таймаут, по умолчанию 3 с |
| `Task<Mvk01CommandResult> SaveConfigurationAsync(CancellationToken cancellationToken = default)` | Сохраняет текущую конфигурацию во Flash (таймаут ответа до 3 с) |
| `Task<Mvk01CommandResult> RestoreConfigurationAsync(CancellationToken cancellationToken = default)` | Восстанавливает конфигурацию из Flash |
| `Task<Mvk01CommandResult> RebootAsync(CancellationToken cancellationToken = default)` | Перезапускает устройство |

Валидация аргументов в реализации `Mvk01Device`: `channel` вне 1–8, `tacho` вне 1–2
и `number` вне 1–184 выбрасывают `ArgumentOutOfRangeException`.

### 7.2 `Mvk01Device`

```csharp
namespace Mvk.MVK01;

public sealed class Mvk01Device : IMvk01Device
```

Типизированный фасад МВК01 поверх `IMvkDevice`: декодирует значения регистров
и выполняет системные команды с ожиданием статуса.

| Сигнатура | Описание |
|---|---|
| `public Mvk01Device(IMvkDevice device, Encoding? textEncoding = null)` | Создаёт фасад устройства; `textEncoding` по умолчанию `Mvk01Codec.DefaultTextEncoding` (cp1251); `device == null` — `ArgumentNullException` |
| `public string Id { get; }` | `device.Settings.Id` |
| `public MvkDeviceSettings Settings { get; }` | `device.Settings` |
| `public MvkDeviceState State { get; }` | `device.State` |
| `public IMvkDevice Device { get; }` | Базовое устройство пула |
| `public Task EnsureConnectedAsync(CancellationToken cancellationToken = default)` | Делегирует базовому устройству |
| `public Task DisconnectAsync(CancellationToken cancellationToken = default)` | Делегирует базовому устройству |
| `public Task<Mvk01DeviceInfo> ReadDeviceInfoAsync(CancellationToken cancellationToken = default)` | 3 запроса: `0x1000` ×12, `0x100C` ×12, `0x1020` ×6 |
| `public Task<Mvk01Channel> ReadChannelAsync(int channel, CancellationToken cancellationToken = default)` | 5 запросов по базе канала |
| `public Task<Mvk01ChannelConfig> ReadChannelConfigAsync(int channel, CancellationToken cancellationToken = default)` | 5 запросов по базе конфигурации канала |
| `public Task<Mvk01TachoMeasurement> ReadTachoAsync(int tacho, CancellationToken cancellationToken = default)` | Один запрос `TachoDataRegisterCount` регистров |
| `public Task<float[]> ReadMeasurementByNumberAsync(Mvk01MeasurementNumber number, CancellationToken cancellationToken = default)` | Приведение к `int` и вызов перегрузки ниже |
| `public Task<float[]> ReadMeasurementByNumberAsync(int number, CancellationToken cancellationToken = default)` | 16 регистров → 8 значений float |
| `public Task<ushort[]> ReadMeasurementRawByNumberAsync(int number, CancellationToken cancellationToken = default)` | 16 сырых регистров параметра |
| `public Task<Mvk01CommandResult> ExecuteSystemCommandAsync(Mvk01SystemCommand command, TimeSpan? timeout = null, CancellationToken cancellationToken = default)` | Запись 2 регистров в `0x0002`, затем опрос статуса |
| `public Task<Mvk01CommandResult> SaveConfigurationAsync(CancellationToken cancellationToken = default)` | `ExecuteSystemCommandAsync(SaveConfiguration)` |
| `public Task<Mvk01CommandResult> RestoreConfigurationAsync(CancellationToken cancellationToken = default)` | `ExecuteSystemCommandAsync(RestoreConfiguration)` |
| `public Task<Mvk01CommandResult> RebootAsync(CancellationToken cancellationToken = default)` | `ExecuteSystemCommandAsync(Reboot)` |

Поведение `ExecuteSystemCommandAsync`:

- команда кодируется `Mvk01Codec.EncodeSystemCommand` с `Settings.ByteOrder`
  и пишется в регистр `Mvk01RegisterMap.SystemCommand` (2 регистра);
- при ошибке записи возвращается `TransportError` с заполненным `Error`;
- статус опрашивается чтением тех же 2 регистров каждые 200 мс до истечения
  общего таймаута (по умолчанию 3 с; таймаут одного опроса — не более 1 с);
- распознаются `StatusSuccess`, `StatusBadCommand`, `StatusFlashError`;
  временные ошибки чтения (`Error.IsTransient`) игнорируются до дедлайна,
  нетранзиентные — возвращают `TransportError`;
- по истечении дедлайна возвращается `Timeout` (без `DeviceStatus`).

```csharp
using Mvk.MVK01;
using Mvk.Modbus.Pool;

MvkDevicePool pool = new(options);
IMvkDevice raw = pool.GetDevice("mvk-1");
Mvk01Device device = new(raw);   // кодировка по умолчанию — cp1251

Mvk01Channel channel = await device.ReadChannelAsync(1, ct);
float rms = channel.RmsSets[2].Acceleration;
```

### 7.3 `Mvk01Connection`

```csharp
namespace Mvk.MVK01;

public sealed class Mvk01Connection : IAsyncDisposable
```

Простейшая точка входа для одного устройства: подключение по адресу без конфигурации.
Создаёт собственный `MvkDevicePool` с `RequireEvenAddressAndQuantity = true`,
`AllowConfigurationWrite = allowConfigurationWrite` (по умолчанию `false`) и
`Pool.ReconnectEnabled = true`; устройство регистрируется с
`Id = Name = Host = host.Trim()`.

| Сигнатура | Описание |
|---|---|
| `public static Task<Mvk01Connection> ConnectAsync(string host, int port = 502, byte slaveId = 1, CancellationToken cancellationToken = default, bool allowConfigurationWrite = false)` | Подключается к устройству МВК01 по адресу и проверяет связь; пустой `host` — `ArgumentException`, недоступное устройство — `MvkModbusException`; при сбое пул освобождается. `allowConfigurationWrite` разрешает запись в защищённую область и системные команды |
| `public IMvk01Device Device { get; }` | Типизированное устройство (доступ к расширенному API и событиям состояния) |
| `public Task<Mvk01DeviceInfo> ReadDeviceInfoAsync(CancellationToken cancellationToken = default)` | Читает общие сведения об устройстве |
| `public Task<Mvk01Channel> ReadChannelAsync(int channel, CancellationToken cancellationToken = default)` | Читает полный снимок канала АЦП |
| `public Task<Mvk01ChannelConfig> ReadChannelConfigAsync(int channel, CancellationToken cancellationToken = default)` | Читает конфигурацию канала АЦП |
| `public Task<Mvk01TachoMeasurement> ReadTachoAsync(int tacho, CancellationToken cancellationToken = default)` | Читает снимок канала тахометра |
| `public Task<Mvk01CommandResult> ExecuteSystemCommandAsync(Mvk01SystemCommand command, TimeSpan? timeout = null, CancellationToken cancellationToken = default)` | Выполняет системную команду с ожиданием статуса |
| `public Task<Mvk01CommandResult> SaveConfigurationAsync(CancellationToken cancellationToken = default)` | Сохраняет конфигурацию во Flash |
| `public ValueTask DisposeAsync()` | Закрывает соединение и освобождает пул |

`Mvk01Connection` не содержит методов `RestoreConfigurationAsync`/`RebootAsync` —
для них используйте `Device.RestoreConfigurationAsync(...)`/`Device.RebootAsync(...)`.

```csharp
using Mvk.MVK01;

await using Mvk01Connection mvk = await Mvk01Connection.ConnectAsync("10.20.0.211");

Mvk01DeviceInfo info = await mvk.ReadDeviceInfoAsync();
Mvk01Channel channel = await mvk.ReadChannelAsync(1);

// Команды Restore/Reboot доступны через свойство Device:
Mvk01CommandResult restored = await mvk.Device.RestoreConfigurationAsync();
```

### 7.4 `IMvk01DeviceFactory`

```csharp
namespace Mvk.MVK01;

public interface IMvk01DeviceFactory
```

Фабрика типизированных устройств МВК01 поверх пула.

| Сигнатура | Описание |
|---|---|
| `IMvk01Device GetDevice(string id)` | Возвращает типизированное устройство по идентификатору из конфигурации; не найдено — `MvkModbusException` (`MvkErrorKind.DeviceNotFound`) |
| `bool TryGetDevice(string id, out IMvk01Device device)` | Пробует получить типизированное устройство по идентификатору |
| `IReadOnlyCollection<IMvk01Device> Devices { get; }` | Все устройства пула в типизированном виде |

### 7.5 `Mvk01DeviceFactory`

```csharp
namespace Mvk.MVK01;

public sealed class Mvk01DeviceFactory : IMvk01DeviceFactory
```

Создаёт и кэширует типизированные фасады для устройств пула.

| Сигнатура | Описание |
|---|---|
| `public Mvk01DeviceFactory(IMvkDevicePool pool, Encoding? textEncoding = null)` | Создаёт фабрику; `pool == null` — `ArgumentNullException`; `textEncoding` по умолчанию `Mvk01Codec.DefaultTextEncoding` (cp1251) |
| `public IMvk01Device GetDevice(string id)` | Фасад по идентификатору; созданные фасады кэшируются по `Settings.Id` (сравнение без учёта регистра) |
| `public bool TryGetDevice(string id, out IMvk01Device device)` | `false` и `device = null!`, если устройства нет в пуле; иначе — результат `GetDevice` |
| `public IReadOnlyCollection<IMvk01Device> Devices { get; }` | Материализованный список фасадов всех устройств пула |

```csharp
using Mvk.MVK01;
using Mvk.Modbus.Pool;

IMvkDevicePool pool = /* ... */;
Mvk01DeviceFactory factory = new(pool, Encoding.GetEncoding(1251));

IMvk01Device device = factory.GetDevice("mvk-1");
if (factory.TryGetDevice("mvk-2", out IMvk01Device? other))
{
    Mvk01Channel channel = await other.ReadChannelAsync(1);
}

foreach (IMvk01Device item in factory.Devices)
{
    Console.WriteLine($"{item.Id}: {item.State}");
}
```

> Кодировка по умолчанию — `Mvk01Codec.DefaultTextEncoding` (cp1251); при
> недоступном провайдере кодовых страниц происходит откат в Latin1. Передайте
> кодировку явно, если нужна другая.

---

## 8. Mvk.MVK01.DependencyInjection

```csharp
namespace Mvk.MVK01.DependencyInjection;

public static class Mvk01ServiceCollectionExtensions
```

Регистрация типизированного слоя МВК01 в DI-контейнере.

| Сигнатура | Описание |
|---|---|
| `public static IServiceCollection AddMvk01Devices(this IServiceCollection services)` | Регистрирует фабрику типизированных устройств МВК01 и включает обязательную настройку чётности; `services == null` — `ArgumentNullException` |

Что делает метод:

1. добавляет `PostConfigure<MvkModbusOptions>`, который после обычной конфигурации
   принудительно выставляет `options.Defaults.RequireEvenAddressAndQuantity = true`
   — чётные адрес и количество регистров (требование устройства).
   `AllowConfigurationWrite` не меняется: запись по умолчанию запрещена и
   включается явно (`Defaults.AllowConfigurationWrite = true`);
2. регистрирует `IMvk01DeviceFactory` как singleton через `TryAddSingleton`:
   `new Mvk01DeviceFactory(provider.GetRequiredService<IMvkDevicePool>())`
   (кодировка по умолчанию — `Mvk01Codec.DefaultTextEncoding`, cp1251).

Вызывать после `AddMvkModbus` — фабрике нужен зарегистрированный `IMvkDevicePool`.

```csharp
using Mvk.MVK01;
using Mvk.MVK01.DependencyInjection;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile("mvkmodbus.json", optional: false);

builder.Services.AddMvkModbus(builder.Configuration);   // пул + конфигурация + валидация
builder.Services.AddMvk01Devices();                     // типизированный слой МВК01

using IHost host = builder.Build();
await host.StartAsync();

IMvk01DeviceFactory factory = host.Services.GetRequiredService<IMvk01DeviceFactory>();
IMvk01Device device = factory.GetDevice("mvk-1");
Mvk01Channel channel = await device.ReadChannelAsync(1);
```

---

## 9. Mvk.MVK01.Polling

Типы для периодического наблюдения за параметрами. Объявлены в
`src/Mvk.MVK01/Polling`. Это надстройка над `IMvk01Device`, низкоуровневый
API остаётся доступен.

### 9.1 `Mvk01PollItem` (`sealed record`)

```csharp
namespace Mvk.MVK01.Polling;

public sealed record Mvk01PollItem
{
    public Mvk01PollItem(Mvk01MeasurementNumber number, int? channel = null);

    public Mvk01MeasurementNumber Number { get; }
    public int? Channel { get; }
}
```

| Сигнатура | Описание |
|---|---|
| `public Mvk01PollItem(Mvk01MeasurementNumber number, int? channel = null)` | Наблюдаемый параметр; `number` — 1–184, `channel` — 1–8 или `null` для всех каналов |
| `public Mvk01MeasurementNumber Number { get; }` | Номер параметра |
| `public int? Channel { get; }` | Выбранный канал для `Mvk01Sample.ChannelValue` |

`number` вне 1–184 или `channel` вне 1–8 — `ArgumentOutOfRangeException`.

### 9.2 `Mvk01Sample` (`sealed record`)

```csharp
public sealed record Mvk01Sample
{
    public required DateTimeOffset Timestamp { get; init; }
    public required Mvk01PollItem Item { get; init; }
    public required float[] Values { get; init; }
    public MvkModbusException? Error { get; init; }

    public bool IsSuccess { get; }
    public float? ChannelValue { get; }
}
```

| Сигнатура | Описание |
|---|---|
| `DateTimeOffset Timestamp` | Время получения результата (UTC) |
| `Mvk01PollItem Item` | Наблюдаемый параметр |
| `float[] Values` | Значения по всем 8 каналам; при ошибке — пустой массив |
| `MvkModbusException? Error` | Ошибка обращения либо `null` при успехе |
| `bool IsSuccess` | `Error is null` |
| `float? ChannelValue` | Значение `Item.Channel` либо `null`, если канал не задан или данных нет |

### 9.3 `Mvk01Watcher` (`sealed class`)

```csharp
public sealed class Mvk01Watcher
{
    public Mvk01Watcher(IMvk01Device device);
    public Mvk01Watcher(IMvk01Device device, TimeSpan interval);

    public IMvk01Device Device { get; }
    public TimeSpan Interval { get; }

    public IAsyncEnumerable<Mvk01Sample> WatchAsync(
        Mvk01PollItem item,
        CancellationToken cancellationToken = default);

    public IAsyncEnumerable<Mvk01Sample> WatchAsync(
        IReadOnlyList<Mvk01PollItem> items,
        [EnumeratorCancellation] CancellationToken cancellationToken = default);
}
```

| Сигнатура | Описание |
|---|---|
| `public Mvk01Watcher(IMvk01Device device)` | Интервал по умолчанию — 500 мс; `device == null` — `ArgumentNullException` |
| `public Mvk01Watcher(IMvk01Device device, TimeSpan interval)` | `interval <= 0` — `ArgumentOutOfRangeException` |
| `public IMvk01Device Device { get; }` | Наблюдаемое устройство |
| `public TimeSpan Interval { get; }` | Интервал между циклами |
| `WatchAsync(Mvk01PollItem item, ...)` | Наблюдение за одним параметром; `item == null` — `ArgumentNullException` |
| `WatchAsync(IReadOnlyList<Mvk01PollItem> items, ...)` | Наблюдение за набором; пустой список или `null`-элемент — `ArgumentException` |

Поведение:

- первый опрос выполняется сразу;
- интервал отсчитывается от начала цикла; если опрос дольше интервала,
  следующий цикл начинается сразу, но запросы не накладываются;
- за один цикл выполняется по одному запросу `ReadMeasurementByNumberAsync`
  на каждый элемент списка; результаты возвращаются по мере готовности;
- `MvkModbusException` передаётся в `Mvk01Sample.Error`, поток продолжается;
- отмена токена приводит к `OperationCanceledException`;
- `Values` всегда содержит 8 значений, даже если выбран один канал.

```csharp
using Mvk.MVK01.Polling;

Mvk01Watcher watcher = new(device, TimeSpan.FromMilliseconds(500));

await foreach (Mvk01Sample sample in watcher.WatchAsync(
    new Mvk01PollItem(Mvk01MeasurementNumber.Rms10To1000Acceleration, channel: 1),
    cancellationToken))
{
    if (sample.Error is { } error)
    {
        Console.WriteLine($"ошибка: {error.ErrorKind} — {error.Message}");
        continue;
    }

    Console.WriteLine($"{sample.Timestamp.ToLocalTime():HH:mm:ss.fff}  {sample.ChannelValue:F4} м/с2");
}
```

---

## См. также

- [Справочник Mvk.Modbus](12a-api-reference-mvk-modbus.md) — конфигурация, пул, транспорт, пайплайн, `RegisterCodec`.
- [Карта регистров](07a-mvk01-register-map-and-codec.md) — разбор адресного пространства, кодеков и особенностей МВК01.
- [README](../README.md) — обзор библиотеки, быстрый старт и настройка.
