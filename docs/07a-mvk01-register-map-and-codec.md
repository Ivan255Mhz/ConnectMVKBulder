# 07a. Карта регистров и кодеки МВК01

Глава описывает «нижний» слой типизированного API МВК01: адресацию регистров
(`Mvk01RegisterMap`, `Mvk01Enums`, `Mvk01MeasurementNumber`, `Mvk01SpectrumBands`)
и декодирование значений (`Mvk01Codec`, `Mvk01Values`). Это фундамент, на котором
построен фасад устройства — готовые вызовы `ReadChannelAsync`, `ReadTachoAsync`,
`ReadMeasurementByNumberAsync` (см. [Фасад МВК01](07b-mvk01-device-facade.md)).

Источник истины — [MVK01_registers.txt](../MVK01_registers.txt) (ПО 1.0.61).
Поведение проверено на реальном устройстве 10.20.0.211 (ПО 1.0.67.1); отличия
и особые случаи отмечены по тексту и собраны в конце главы.

Содержание:

1. [Адресное пространство](#1-адресное-пространство)
2. [Mvk01RegisterMap — справочник констант и формул](#2-mvk01registermap--справочник-констант-и-формул)
3. [Адреса — регистры, длины — байты](#3-адреса--регистры-длины--байты)
4. [Mvk01Enums — перечисления](#4-mvk01enums--перечисления)
5. [Mvk01MeasurementNumber — параметры по номерам](#5-mvk01measurementnumber--параметры-по-номерам)
6. [Mvk01SpectrumBands — полосы дольоктавного спектра](#6-mvk01spectrumbands--полосы-дольоктавного-спектра)
7. [Mvk01Codec — кодирование и декодирование](#7-mvk01codec--кодирование-и-декодирование)
8. [Mvk01Values — структуры значений](#8-mvk01values--структуры-значений)
9. [Примеры](#9-примеры)
10. [Особенности, проверенные на устройстве](#10-особенности-проверенные-на-устройстве)
11. [Подводные камни](#подводные-камни)
12. [См. также](#см-также)

---

## 1. Адресное пространство

Вся карта МВК01 — 16-битные регистры Modbus. Области верхнего уровня:

| Диапазон | Область | Доступ |
|---|---|---|
| `0x0000–0x0FFF` | Конфигурационные параметры | чтение и запись |
| `0x1000–0x1FFF` | Системные параметры | только чтение |
| `0x2000–0x3FFF` | Расчётные параметры (по каналам) | только чтение |
| `0x4000–0x4FFF` | Расчётные параметры (по номерам) | только чтение |
| `0x8000–0x8FFF` | Конфигурация расширенной диагностики | чтение и запись (в документе не описывается) |
| `0x9000–0xA000` | Результаты расширенной диагностики | только чтение (в документе не описывается) |

Внутри областей:

| Подобласть | Адреса | Шаг блока | Блоков |
|---|---|---|---|
| Общие параметры устройства (3.1.1) | `0x0000–0x00FF` | — | — |
| Конфигурация каналов АЦП (3.2.1) | `0x0100–0x08FF` | `0x0100` | 8 |
| Конфигурация тахометров (3.3.1) | `0x0900–0x0AFF` | `0x0100` | 2 |
| Системные параметры (4.1.1) | `0x1000–0x1043` (описанные поля) | — | — |
| Расчётные параметры каналов АЦП (5.1.1) | `0x2000–0x2FFF` | `0x0200` | 8 |
| Расчётные параметры тахометров (5.2.1) | `0x3000–0x31FF` | `0x0100` | 2 |
| Параметры по номерам (5.1.2) | `0x4000 + (N−1)×16`, N = 1…184 | `16` | 184 |

Дополнительно: фаза и амплитуда 1–4 гармоник частоты вращения лежат за пределами
блока канала — по абсолютным смещениям `+0x7034`/`+0x703C` от базы канала
(то есть фактически в области `0x9000+`).

---

## 2. Mvk01RegisterMap — справочник констант и формул

`Mvk01RegisterMap` (`src/Mvk.MVK01/Mvk01RegisterMap.cs`) — статический класс только
с константами и чистыми функциями адресации. Никаких обращений к устройству он не делает.

### 2.1 Общие ограничения

| Константа | Значение | Смысл |
|---|---|---|
| `MaxChannels` | 8 | Максимальное число каналов АЦП |
| `MaxTachoChannels` | 2 | Максимальное число каналов тахометра |
| `SpectrumBands` | 43 | Число полос дольоктавного спектра |
| `MaxMeasurementNumber` | 184 | Максимальный номер параметра (раздел 5.1.2) |

### 2.2 Раздел 3.1.1 — общие параметры устройства (чтение/запись)

| Константа | Адрес | Формат | Примечание |
|---|---|---|---|
| `SystemCommand` | `0x0002` | uint32 | Системная команда/статус выполнения, см. 3.1.2 |
| `IpAddress` | `0x0004` | 4 байта | IP-адрес устройства |
| `SubnetMask` | `0x0006` | 4 байта | Маска подсети |
| `Gateway` | `0x0008` | 4 байта | Адрес основного шлюза |
| `ModbusPort` | `0x000A` | uint32 | TCP-порт Modbus/TCP (1–65535) |
| `ByteOrder` | `0x000C` | uint32 | Порядок передачи байт клиента, см. 3.1.4 |
| `SampleRate` | `0x000E` | uint32 | Частота дискретизации DSP (только чтение) |
| `MaxRpmFrequency` | `0x0010` | float, Гц | Максимальное значение частоты вращения |
| `MinRpmFrequency` | `0x0012` | float, Гц | Минимальное значение частоты вращения |
| `ControlMode` | `0x0014` | uint32 | Режим контроля и диагностики, см. 3.1.5 |
| `ChannelsPerUnit` | `0x0016` | uint32 | Число каналов 1-го агрегата (1–7; 0 = по умолчанию 4) |
| `ClientTimeout` | `0x0018` | uint32, с | Таймаут неактивности Modbus-клиента (3–3600; 0 = 3 с) |
| `SpectrumDataType` | `0x001A` | uint32 | Тип данных спектра: 0 — ускорение, 1 — скорость, 2 — перемещение |
| `AdcCalibration` | `0x0020` | 8 × float | Калибровочные коэффициенты каналов АЦП (по 4 байта на канал) |
| `UartModuleType` | `0x0040` | uint32 | Тип дополнительного модуля UART (P7) |

Адрес `0x0000` (конфигурация DSP) в документе помечен как «только чтение, не писать»
и отдельной константой в `Mvk01RegisterMap` не представлен.

Форматы из документа:

| Значение | Смысл |
|---|---|
| `0x10001` | Команда: восстановить конфигурацию из Flash |
| `0xF0001` | Команда: сохранить конфигурацию во Flash (таймаут ответа до 3 с) |
| `0xA0001` | Команда: перезапустить устройство |
| `0x00000` | Статус: успешно выполнено |
| `0x00BAD` | Статус: клиент записал неверную команду |
| `0xF0BAD` | Статус: ошибка флэш-памяти, конфигурация не сохранена |

Порядок байт (`ByteOrder`, регистр `0x000C`):

| Значение регистра | `MvkByteOrder` |
|---|---|
| `0x00000000` | `BigEndian` (по умолчанию) |
| `0x99999999` | `Mixed` (для ВДМ82) |
| `0x66666666` | `LittleEndian` |

### 2.3 Раздел 3.2.1 — конфигурация измерительных каналов АЦП (чтение/запись)

Блок на канал — `ChannelConfigBlockSize = 0x0100` регистров (256). База канала:
`A = 0x100 + (N−1)×0x100`; метод — `ChannelConfig(N)`.

| Канал | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
|---|---|---|---|---|---|---|---|---|
| База `A` | `0x0100` | `0x0200` | `0x0300` | `0x0400` | `0x0500` | `0x0600` | `0x0700` | `0x0800` |

Смещения внутри блока (константы заданы относительно `A`):

| Константа | Смещение | Формат | Примечание |
|---|---|---|---|
| `ChannelTypeOffset` | `+0x0000` | uint32 | Тип канала, на МВК01 = 0 |
| `ChannelSensorFactorOffset` | `+0x0002` | float | Коэффициент преобразования датчика, В/(м/с2) |
| `ChannelOverloadThresholdOffset` | `+0x0004` | float | Порог перегрузки в долях (1.0 = макс. амплитуда) |
| `ChannelConversionFactorOffset` | `+0x000E` | float | Резерв: коэффициент пересчёта, не писать — считает устройство |
| `ChannelUnitsOffset` | `+0x0010` | 32 байта (16 рег.) | Наименование единиц, по умолчанию «м/с2», cp1251 + swap |
| `ChannelSerialOffset` | `+0x0020` | 32 байта (16 рег.) | Серийный номер датчика, cp1251 + swap |
| `ChannelFilterX25kHzOffset` | `+0x003E` | uint32 | Фильтр X…25 кГц — номер нижней частоты среза (0–3) |
| `ChannelFilter1LowOffset` | `+0x0040` | uint32 | Фильтр 1 — номер нижней дольоктавы (1–43) |
| `ChannelFilter1HighOffset` | `+0x0042` | uint32 | Фильтр 1 — номер верхней дольоктавы |
| `ChannelFilter2LowOffset` | `+0x0044` | uint32 | Фильтр 2 — номер нижней дольоктавы |
| `ChannelFilter2HighOffset` | `+0x0046` | uint32 | Фильтр 2 — номер верхней дольоктавы |
| `ChannelMaxDcVoltageOffset` | `+0x0050` | float, В | Максимально допустимое постоянное напряжение на датчике |
| `ChannelMinDcVoltageOffset` | `+0x0052` | float, В | Минимально допустимое постоянное напряжение на датчике |
| `ChannelMainControlledParamOffset` | `+0x0060` | 32 байта (16 рег.) | Конфигурация основного контролируемого параметра |
| `ChannelControlledParamsOffset` | `+0x0070` | 7 × 32 байта | Конфигурация контролируемых параметров 2–8 |

Один блок конфигурации контролируемого параметра (раздел 3.2.2) занимает
`ControlledParamSize = 16` регистров (32 байта) и состоит из восьми 4-байтных полей:
номер параметра (0 = не контролируется), нижняя граница жёлтой зоны V1,
нижняя граница красной зоны V2, время перехода в зону выше T↑ (с),
время перехода в зону ниже T↓ (с), гистерезис жёлтой зоны H1, гистерезис красной
зоны H2, верхний предел шкалы индикации.

### 2.4 Раздел 3.3.1 — конфигурация каналов тахометра (чтение/запись)

Блок на канал — `TachoConfigBlockSize = 0x0100` регистров. База:
`A = 0x900 + (N−1)×0x100`; метод — `TachoConfig(N)`.

| Тахометр | 1 | 2 |
|---|---|---|
| База `A` | `0x0900` | `0x0A00` |

| Константа | Смещение | Формат | Примечание |
|---|---|---|---|
| `TachoConfigHysteresisOffset` | `+0x0006` | uint32 | Гистерезис (мин. время срабатывания) в 1/65536 с |
| `TachoConfigAdditionalFunctionOffset` | `+0x0008` | uint32 | 0 — нет, 1 — кнопка квитирования |
| `TachoConfigSerialOffset` | `+0x0020` | 32 байта (16 рег.) | Серийный номер датчика тахометра |

Тип канала (`A+0x00`, должен быть 0) отдельной константой не вынесен.

### 2.5 Раздел 4.1.1 — системные параметры, общие (только чтение)

| Константа | Адрес | Формат | Примечание |
|---|---|---|---|
| `DeviceSerialNumber` | `0x1000` | 8 байт (4 рег.) | Серийный номер устройства, прямой порядок строки |
| `FirmwareVersion` | `0x1004` | uint32 | Версия ПО, см. 4.1.2 |
| `SystemErrorIndicators` | `0x1006` | uint32 | Индикатор системных ошибок, см. 4.1.3 |
| `Uptime` | `0x1008` | uint32, с | Счётчик времени работы |
| `MemoryCardSize` | `0x100A` | uint32, КБ | Размер установленной карты памяти |
| `DspMd5` | `0x100C` | 16 байт (8 рег.) | MD5-хэш внутреннего ПО DSP |
| `Unit1ControlState` | `0x1014` | uint32 | Состояние контроля по агрегату 1, см. 4.1.4 |
| `Unit2ControlState` | `0x1016` | uint32 | Состояние контроля по агрегату 2, см. 4.1.4 |
| `DspIndicators` | `0x1020` | 3 × 4 байта (6 рег.) | Индикаторы работы DSP, см. 4.1.5 |

### 2.6 Раздел 5.1.1 — расчётные параметры каналов АЦП (только чтение)

Блок на канал — `ChannelDataBlockSize = 0x0200` регистров (512). База:
`A = 0x2000 + (N−1)×0x200`; метод — `ChannelData(N)`.

| Канал | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
|---|---|---|---|---|---|---|---|---|
| База `A` | `0x2000` | `0x2200` | `0x2400` | `0x2600` | `0x2800` | `0x2A00` | `0x2C00` | `0x2E00` |

Смещения внутри блока (константы заданы относительно `A`):

| Константа | Смещение | Регистров | Формат | Примечание |
|---|---|---|---|---|
| `SensorVoltageOffset` | `+0x0000` | 2 | float, В | Напряжение на датчике |
| `SpectrumOffset` | `+0x0002` | `SpectrumRegisterCount` = 86 | 43 × float | Дольоктавный спектр, полосы 1–43 |
| `PeakFactorOffset` | `+0x005C` | 2 | float | Пик-фактор виброускорения X…25 кГц |
| `RmsX25kHzOffset` | `+0x005E` | 2 | float, м/с2 | СКЗ виброускорения X…25 кГц |
| `RmsSetOffset` | `+0x0060` | `RmsSetRegisterCount` = 48 | 8 × 3 × float | Наборы СКЗ (ускорение, скорость, перемещение) |
| `DcComponentOffset` | `+0x009A` | 2 | float, м/с2 | Постоянная составляющая в канале |
| `SensorVoltageCopyOffset` | `+0x009C` | 2 | float, В | Копия напряжения на датчике (параметр 79) |
| `ChannelStateOffset` | `+0x009E` | 2 | uint32 | Индикаторы состояния канала, см. 5.1.4 |
| `EquivalentPeakOffset` | `+0x00A0` | `EquivalentRegisterCount` = 48 | `EquivalentValuesCount` = 24 × float | Эквивалентные пиковые значения (аналог параметров 49–72) |
| `EquivalentRangeOffset` | `+0x00E0` | 48 | 24 × float | Эквивалентные значения размаха |
| `ChannelStateCopyOffset` | `+0x0120` | 2 | uint32 | Копия индикаторов состояния канала |
| `ControlIndicatorsOffset` | `+0x0130` | `ControlIndicatorsRegisterCount` = 64 | 8 × 8 рег. | Индикаторы контроля параметров 1–8, см. 5.1.5 |

Наборы СКЗ (`RmsSetOffset`) — 8 наборов по 3 значения (`RmsSetCount = 8`,
`RmsSetValues = 3`), порядок наборов: 10…3000 Гц, 10…2000 Гц, 10…1000 Гц,
2…1000 Гц, 0.8…300 Гц, 0.8…150 Гц, Фильтр 1, Фильтр 2. Внутри набора:
ускорение (м/с2), скорость (мм/с), перемещение (мкм).

Блок индикаторов одного контролируемого параметра — `ControlIndicatorSize = 8`
регистров (16 байт): текущее значение (float), состояние контроля (формат 4.1.4),
значение при последней смене состояния (float), резерв.

### 2.7 Раздел 5.1.2 — расчётные параметры по номерам (только чтение)

| Константа | Значение | Смысл |
|---|---|---|
| `MeasurementByNumberBase` | `0x4000` | База блока |
| `MeasurementByNumberStride` | 16 | Шаг между параметрами (8 каналов × 2 регистра) |
| `MeasurementByNumberRegisterCount` | 16 | Регистров на один параметр |
| `MeasurementByNumberChannels` | 8 | Каналов в блоке параметра |

Формула: `измерение(N) = 0x4000 + (N−1)×16`, канал `i` (1–8) — пара регистров
со смещением `(i−1)×2` внутри блока. Дублирует раздел 5.1.1 (кроме амплитуды и фазы
гармоник) и позволяет прочитать один параметр сразу по всем 8 каналам одним запросом.

### 2.8 Раздел 5.2.1 — расчётные параметры каналов тахометра (только чтение)

Блок на канал — `TachoDataBlockSize = 0x0100`. База: `A = 0x3000 + (N−1)×0x100`;
метод — `TachoData(N)`.

| Тахометр | 1 | 2 |
|---|---|---|
| База `A` | `0x3000` | `0x3100` |

| Константа | Смещение | Формат | Примечание |
|---|---|---|---|
| `TachoFrequencyOffset` | `+0x0000` | float, Гц | Частота вращения |
| `TachoAverageIncrementOffset` | `+0x0002` | float, Гц | Среднее приращение частоты вращения на оборот |
| `TachoSensorErrorOffset` | `+0x0004` | uint32 | 1, если период меньше минимально допустимого |
| `TachoTimestampOffset` | `+0x0006` | uint32, 1/65536 с | Метка времени последнего перехода (с гистерезисом) |
| `TachoInputLevelOffset` | `+0x0008` | uint32 | Текущий уровень входа, 0 или 1 |
| `TachoDirectionOffset` | `+0x000A` | uint32 | Направление вращения, только 1-й тахометр, см. 5.2.2 |
| `TachoTransitionsOffset` | `+0x000C` | uint32 | Счётчик переходов 0→1 (циклический) |

Полезных данных — 14 регистров (`TachoDataRegisterCount = 0x000E`).

### 2.9 Гармоники частоты вращения

| Константа | Смещение от базы канала | Формат |
|---|---|---|
| `HarmonicsPhaseOffset` | `+0x7034` | 4 × float |
| `HarmonicsAmplitudeOffset` | `+0x703C` | 4 × float |

Смещения заданы от базы канала и выводят за пределы 512-регистрового блока:
адрес по-прежнему считается по формуле `A = 0x2000 + (N−1)×0x200`.
Например, для канала 1: фаза — `0x9034`, амплитуда — `0x903C`; для канала 2 —
`0x9234`/`0x923C`. Это область `0x9000+` (результаты расширенной диагностики),
в разделе 5.1.2 гармоники не дублируются.

### 2.10 Формулы-методы

| Метод | Формула | Проверка аргумента |
|---|---|---|
| `ChannelConfig(channel)` | `0x100 + (channel−1)×0x100` | нет |
| `ChannelData(channel)` | `0x2000 + (channel−1)×0x200` | нет |
| `TachoConfig(tacho)` | `0x900 + (tacho−1)×0x100` | нет |
| `TachoData(tacho)` | `0x3000 + (tacho−1)×0x100` | нет |
| `MeasurementByNumber(number)` | `0x4000 + (number−1)×16` | нет |

Методы не проверяют диапазон: за него отвечает вызывающий код (типизированный слой
валидирует номера каналов 1–8, тахометров 1–2 и параметров 1–184).

Примеры вычисления:

| Вызов | Результат |
|---|---|
| `ChannelConfig(1)` | `0x0100` |
| `ChannelConfig(4)` | `0x0400` |
| `ChannelConfig(8)` | `0x0800` |
| `ChannelData(1)` | `0x2000` |
| `ChannelData(4)` | `0x2600` |
| `ChannelData(8)` | `0x2E00` |
| `TachoConfig(1)` | `0x0900` |
| `TachoConfig(2)` | `0x0A00` |
| `TachoData(1)` | `0x3000` |
| `TachoData(2)` | `0x3100` |
| `MeasurementByNumber(1)` | `0x4000` |
| `MeasurementByNumber(2)` | `0x4010` |
| `MeasurementByNumber(48)` | `0x42F0` |
| `MeasurementByNumber(80)` | `0x44F0` |
| `MeasurementByNumber(184)` | `0x4B70` |

Пример адреса поля: напряжение на датчике канала 4 — `ChannelData(4) + SensorVoltageOffset
= 0x2600 + 0x0000 = 0x2600`; состояние канала 8 — `0x2E00 + 0x9E = 0x2E9E`.

---

## 3. Адреса — регистры, длины — байты

В `MVK01_registers.txt` адреса всегда указаны **в 16-битных регистрах**, а длины —
**в байтах** (у строк — `8×4, char` и т.п.). Пересчёт: `1 регистр = 2 байта`,
`4 байта = 2 регистра`. Ошибка в 2 раза здесь — самая частая причина «сдвинутых»
показаний.

| Поле | Адрес | Длина в документе | Регистров | Примечание |
|---|---|---|---|---|
| Единицы измерения канала | `A+0x10` | 32 байта | 16 | swap + cp1251 |
| Серийный номер датчика канала | `A+0x20` | 32 байта | 16 | swap + cp1251 |
| Серийный номер датчика тахометра | `A+0x20` | 32 байта | 16 | swap + cp1251 |
| Серийный номер устройства | `0x1000` | 8 байт | 4 | прямой порядок |
| MD5-хэш ПО DSP | `0x100C` | 16 байт | 8 | hex-строка |
| Дольоктавный спектр (одна полоса) | `A+0x02` | 4 байта | 2 | float |
| Дольоктавный спектр (весь) | `A+0x02` | 43×4 = 172 байта | 86 | 43 float |
| Наборы СКЗ | `A+0x60` | 8×3×4 = 96 байт | 48 | 24 float |
| Эквивалентные значения | `A+0xA0`, `A+0xE0` | 24×4 = 96 байт | 48 | 24 float |
| Индикаторы контроля параметров | `A+0x130` | 8×16 = 128 байт | 64 | 8 блоков по 8 рег. |
| Конфигурация контролируемого параметра | `A+0x60`, `A+0x70` | 32 байта | 16 | 8 четырёхбайтных полей |
| Параметр по номеру | `0x4000+(N−1)×16` | 8×4 = 32 байта | 16 | 8 каналов |

Пример пересчёта: поле единиц измерения канала 1 начинается с адреса
`ChannelConfig(1) + ChannelUnitsOffset = 0x0100 + 0x10 = 0x0110`; в документе длина
32 байта, значит читать нужно 16 регистров:

```csharp
ushort[] units = await device.ReadHoldingRegistersAsync(
    (ushort)(Mvk01RegisterMap.ChannelConfig(1) + Mvk01RegisterMap.ChannelUnitsOffset),
    16,
    ct);
string text = Mvk01Codec.DecodeTextSwapped(units, 32);   // «м/с2»
```

Другой пример: весь дольоктавный спектр — 86 регистров с адреса `A+0x02`; в один запрос
Modbus укладывается максимум 125 регистров, то есть 86 помещаются целиком.

---

## 4. Mvk01Enums — перечисления

Все перечисления — из `src/Mvk.MVK01/Mvk01Enums.cs`.

### `Mvk01SystemCommand` (uint, регистр `0x0002`)

| Член | Значение | Смысл |
|---|---|---|
| `RestoreConfiguration` | `0x10001` | Восстановить конфигурацию из Flash |
| `SaveConfiguration` | `0xF0001` | Сохранить конфигурацию во Flash; таймаут ответа увеличить до 3 с |
| `Reboot` | `0xA0001` | Перезапустить устройство |

### `Mvk01CommandStatus`

| Член | Значение | Смысл |
|---|---|---|
| `Success` | `0x00000` | Операция успешно выполнена |
| `BadCommand` | `0x00BAD` | Клиент записал неверную команду |
| `FlashError` | `0xF0BAD` | Ошибка флэш-памяти, конфигурация не сохранена |
| `Pending` | `−1` | Команда принята, статус ещё не определён (продолжается ожидание) |
| `Timeout` | `−2` | Истёк таймаут ожидания статуса |
| `TransportError` | `−3` | Ошибка транспорта при отправке/чтении статуса |

### `Mvk01Zone` (byte, раздел 4.1.4, биты 0–7)

| Член | Значение | Смысл |
|---|---|---|
| `Undefined` | `0x00` | Зона не определена |
| `Green` | `0x11` | Зелёная зона |
| `SensorFailure` | `0x12` | Отказ датчика |
| `InvalidConfiguration` | `0x13` | Неверная конфигурация |
| `Yellow` | `0x14` | Жёлтая зона |
| `Red` | `0x15` | Красная зона |

### `Mvk01RotationDirection` (uint, раздел 5.2.2, только 1-й тахометр при энкодере)

| Член | Значение | Смысл |
|---|---|---|
| `BelowMinimum` | `0x00` | Частота меньше минимально допустимой |
| `Tacho1ToTacho2` | `0x02` | Вращение от тахометра 1 к тахометру 2 |
| `Tacho2ToTacho1` | `0x03` | Вращение от тахометра 2 к тахометру 1 |

### `Mvk01SpectrumDataType` (uint, регистр `0x001A`)

| Член | Значение | Смысл |
|---|---|---|
| `Acceleration` | 0 | Ускорение |
| `Velocity` | 1 | Скорость |
| `Displacement` | 2 | Перемещение |

### `Mvk01FilterX25kHz` (uint, смещение канала `+0x3E`)

| Член | Значение | Нижняя частота среза |
|---|---|---|
| `Off` | 0 | Фильтр выключен |
| `Hz6400` | 1 | 6.4 кГц |
| `Hz10000` | 2 | 10 кГц |
| `Hz15000` | 3 | 15 кГц |

### `Mvk01UartModuleType` (uint, регистр `0x0040`)

| Член | Значение | Смысл |
|---|---|---|
| `None` | 0 | Нет |
| `Kpa1V` | 1 | КПА-1В |
| `Vdm82` | 3 | ВДМ82 |

Значение 2 в `Mvk01UartModuleType` не используется (в документе его нет).

---

## 5. Mvk01MeasurementNumber — параметры по номерам

Перечисление `Mvk01MeasurementNumber` содержит основные номера параметров раздела 5.1.2.
Для остальных параметров используйте числовой номер: **любое число 1–184** принимается
методами `MeasurementByNumber` и `ReadMeasurementByNumberAsync`.

| Номер | Член | Параметр |
|---|---|---|
| 1 | `SensorVoltage` | Напряжение на датчике, В |
| 2 | `SpectrumBand1` | Полоса спектра 1 (0.63 Гц) |
| … | … | Полосы 2–42 — числом |
| 44 | `SpectrumBand43` | Полоса спектра 43 (10 кГц) |
| 47 | `PeakFactorAcceleration` | Пик-фактор виброускорения X…25 кГц |
| 48 | `RmsX25kHzAcceleration` | СКЗ виброускорения X…25 кГц, м/с2 |
| 49 | `Rms10To3000Acceleration` | СКЗ ускорения 10…3000 Гц, м/с2 |
| 50 | `Rms10To3000Velocity` | СКЗ скорости 10…3000 Гц, мм/с |
| 51 | `Rms10To3000Displacement` | СКЗ перемещения 10…3000 Гц, мкм |
| 52–54 | `Rms10To2000...` | СКЗ 10…2000 Гц (ускорение, скорость, перемещение) |
| 55–57 | `Rms10To1000...` | СКЗ 10…1000 Гц |
| 58–60 | `Rms2To1000...` | СКЗ 2…1000 Гц |
| 61–63 | `Rms0_8To300...` | СКЗ 0.8…300 Гц |
| 64–66 | `Rms0_8To150...` | СКЗ 0.8…150 Гц |
| 67–69 | `RmsFilter1...` | СКЗ по фильтру 1 |
| 70–72 | `RmsFilter2...` | СКЗ по фильтру 2 |
| 78 | `DcComponent` | Постоянная составляющая в канале, м/с2 |
| 79 | `SensorVoltageCopy` | Напряжение на датчике (копия параметра 1), В |
| 80 | `ChannelState` | Индикаторы состояния канала (uint32, раздел 5.1.4) |
| 81 | `EquivalentPeakStart` | Начало блока эквивалентных пиковых значений (81–104) |
| 123 | `EquivalentRangeStart` | Начало блока эквивалентных значений размаха (123–136) |
| 145 | `ChannelStateCopy` | Копия индикаторов состояния канала |
| 153 | `ControlParameter1Value` | Значение контролируемого параметра 1 (параметры 153–184) |

Правило: диапазон номеров — 1–184, в `Mvk01MeasurementNumber` перечислены только
основные. Любой номер можно передать числом: например, полоса 20 (`50 Гц`) — это
номер 21, а последнее значение размаха — 136.

Резервные номера (105–112, 137–144, 146–152) также адресуются формулой, но данных
не содержат — читать их без необходимости не стоит. Номера 113–122 в таблице 5.1.1
отсутствуют вовсе.

Параметры 45, 46 и 73–77 в блоке канала помечены как резерв.

---

## 6. Mvk01SpectrumBands — полосы дольоктавного спектра

`Mvk01SpectrumBands` (`src/Mvk.MVK01/Mvk01SpectrumBands.cs`) — 43 средние частоты
третьоктавного ряда (раздел 5.1.3). Полоса 1 — 0.63 Гц, полоса 43 — 10 кГц.

| № | Частота, Гц | № | Частота, Гц | № | Частота, Гц | № | Частота, Гц |
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

Членов класса немного:

| Член | Тип | Описание |
|---|---|---|
| `Count` | `int` | Число полос — 43 |
| `CenterFrequencies` | `ReadOnlySpan<double>` | Средние частоты полос (Гц) |
| `GetCenterFrequency(bandNumber)` | `double` | Частота полосы 1–43; вне диапазона — `ArgumentOutOfRangeException` |
| `FormatBand(bandNumber)` | `string` | Подпись: «0.63 Гц», «50 Гц», «1 кГц», «10 кГц» |

```csharp
int bands = Mvk01SpectrumBands.Count;                       // 43
double f1 = Mvk01SpectrumBands.GetCenterFrequency(1);       // 0.63
double f20 = Mvk01SpectrumBands.GetCenterFrequency(20);     // 50.0
double f43 = Mvk01SpectrumBands.GetCenterFrequency(43);     // 10000.0

string label = Mvk01SpectrumBands.FormatBand(43);           // "10 кГц"
// Mvk01SpectrumBands.GetCenterFrequency(44) бросит ArgumentOutOfRangeException
```

Формула `FormatBand`: при частоте `>= 1000` — «{частота / 1000} кГц», иначе
«{частота} Гц». Формат числа — `0.###`, поэтому в русской культуре разделитель
может быть запятой («0,63 Гц»), а в инвариантной — точкой.

---

## 7. Mvk01Codec — кодирование и декодирование

`Mvk01Codec` (`src/Mvk.MVK01/Mvk01Codec.cs`) — статический класс поверх
`RegisterCodec` из `Mvk.Modbus.Protocol`. Методы, зависящие от порядка байт
(`EncodeSystemCommand`, `DecodeDspIndicators`, `DecodeFloats`), принимают
`MvkByteOrder` — берите его из настроек устройства (`device.Settings.ByteOrder`).

| Член | Возвращает | Назначение |
|---|---|---|
| `EncodeSystemCommand(command, byteOrder)` | `ushort[2]` | Системная команда в два регистра |
| `DecodeFirmwareVersion(packed)` | `Mvk01FirmwareVersion` | Версия ПО (4.1.2) |
| `DecodeSystemErrors(raw)` | `Mvk01SystemErrors` | Индикатор системных ошибок (4.1.3) |
| `DecodeUnitState(raw)` | `Mvk01UnitState` | Состояние контроля по агрегату (4.1.4) |
| `DecodeChannelState(raw)` | `Mvk01ChannelState` | Индикаторы состояния канала (5.1.4) |
| `DecodeDspIndicators(registers, byteOrder)` | `Mvk01DspIndicators` | Индикаторы DSP (4.1.5), 6 регистров |
| `DecodeTachoTimestampSeconds(raw)` | `double` | Метка времени тахометра в секундах |
| `DecodeFloats(registers, valueCount, byteOrder)` | `float[]` | Массив float из регистров |
| `DecodeText(registers, byteCount, encoding?)` | `string` | Строка прямого порядка |
| `DecodeTextSwapped(registers, byteCount, encoding?)` | `string` | Строка канала (swap внутри 4-байтных слов) |
| `EncodeTextSwapped(text, registerCount, encoding?)` | `ushort[]` | Обратное к `DecodeTextSwapped` |
| `FormatMd5(md5Bytes)` | `string` | MD5 в нижнем регистре без разделителей |
| `DefaultTextEncoding` | `Encoding` | cp1251, при недоступности — Latin1 |

Константы статусов: `StatusSuccess = 0x00000`, `StatusBadCommand = 0x00BAD`,
`StatusFlashError = 0xF0BAD`.

### 7.1 `EncodeSystemCommand`

Кодирует `Mvk01SystemCommand` в два регистра с учётом порядка байт устройства.

```csharp
ushort[] big = Mvk01Codec.EncodeSystemCommand(
    Mvk01SystemCommand.SaveConfiguration, MvkByteOrder.BigEndian);
// [0x000F, 0x0001]

ushort[] mixed = Mvk01Codec.EncodeSystemCommand(
    Mvk01SystemCommand.SaveConfiguration, MvkByteOrder.Mixed);
// [0x0001, 0x000F]
```

### 7.2 `DecodeFirmwareVersion`

Биты 24–31 — версия, 16–23 — подверсия 1, 8–15 — подверсия 2, 0–7 — сборка.

```csharp
Mvk01FirmwareVersion version = Mvk01Codec.DecodeFirmwareVersion(0x01071400);
Console.WriteLine(version);            // 1.7.20.0
Console.WriteLine(version.Minor);      // 7
```

### 7.3 `DecodeSystemErrors`

| Бит | Поле | Смысл |
|---|---|---|
| 0 | `DspReceiveError` | Ошибка приёма данных от DSP |
| 1–3 | `SdCardError` | Ошибка работы с SD-картой (маска `0b1110`) |
| 4 | `DspSendError` | Ошибка отправки данных к DSP |
| 5 | `ModbusInitializationError` | Ошибка инициализации Modbus/TCP |
| 6 | `ConfigurationCrcError` | Ошибка CRC конфигурации (загружена конфигурация по умолчанию) |

```csharp
Mvk01SystemErrors errors = Mvk01Codec.DecodeSystemErrors(0b0111_0001);
// DspReceiveError = true, SdCardError = false, DspSendError = true,
// ModbusInitializationError = true, ConfigurationCrcError = true

if (!errors.IsClean)
{
    Console.WriteLine($"Системные ошибки: 0x{errors.Raw:X8}");
}
```

`Raw` содержит исходное значение целиком; `IsClean` истинно только при `Raw == 0`.

### 7.4 `DecodeUnitState`

| Биты | Поле |
|---|---|
| 0–7 | `Zone` (`Mvk01Zone`) |
| 9 | `SensorFailureEvent` — отказ датчика |
| 10 | `EnteredYellowZone` — переход в жёлтую зону |
| 11 | `EnteredRedZone` — переход в красную зону |
| 16–31 | Резерв |

```csharp
Mvk01UnitState state = Mvk01Codec.DecodeUnitState(0x14 | (1u << 10) | (1u << 11));
// Zone = Mvk01Zone.Yellow, EnteredYellowZone = true, EnteredRedZone = true
```

### 7.5 `DecodeChannelState`

| Бит | Поле | Смысл |
|---|---|---|
| 0 | `Overload` | Перегрузка измерительного входа |
| 4 | `Buffer16HzNotFilled` | Буфер 16 Гц не заполнен; фильтры < 3.9 Гц не активны |
| 5 | `Buffer64HzNotFilled` | Буфер 64 Гц не заполнен; фильтры < 25 Гц не активны |
| 6 | `Buffer512HzNotFilled` | Буфер 512 Гц не заполнен; все фильтры не активны |
| 10 | `DcVoltageAboveMaximum` | Постоянное напряжение выше нормы (вероятен обрыв кабеля) |
| 11 | `DcVoltageBelowMinimum` | Постоянное напряжение ниже нормы (вероятно КЗ в кабеле) |

```csharp
Mvk01ChannelState state = Mvk01Codec.DecodeChannelState(1u | (1u << 6) | (1u << 11));
// Overload = true, Buffer512HzNotFilled = true, DcVoltageBelowMinimum = true
```

### 7.6 `DecodeDspIndicators`

Требует минимум 6 регистров (`0x1020–0x1025`); при меньшем числе — `ArgumentException`,
лишние регистры игнорируются.
Первая пара — маска перегрузки (младший байт = каналы 1–8), вторая — флаги ошибок
конфигурации (бит 30 — число каналов, бит 31 — режим контроля), третья — счётчик
выполненных процедур расчёта.

```csharp
Mvk01DspIndicators indicators = Mvk01Codec.DecodeDspIndicators(
    [0x0000, 0x0003, 0x0000, 0x0004, 0x0000, 0x002A],
    MvkByteOrder.BigEndian);

indicators.OverloadChannelMask;                 // 0x03
indicators.IsChannelOverloaded(1);              // true
indicators.IsChannelOverloaded(3);              // false
indicators.ConfigurationFlags;                  // 4
indicators.CalculationCounter;                  // 42
```

`IsChannelOverloaded(channel)` проверяет бит `(channel − 1)` маски; вне диапазона
1–8 выбрасывает `ArgumentOutOfRangeException`.

### 7.7 `DecodeTachoTimestampSeconds`

Метка времени — uint32 в 1/65536 с; метод делит на 65536:

```csharp
double seconds = Mvk01Codec.DecodeTachoTimestampSeconds(65536);   // 1.0
double half = Mvk01Codec.DecodeTachoTimestampSeconds(32768);      // 0.5
```

### 7.8 `DecodeFloats`

Декодирует `valueCount` значений float из регистров (по 2 регистра на значение);
если регистров меньше `valueCount × 2` — `ArgumentException`.

```csharp
float[] values = Mvk01Codec.DecodeFloats(
    [0x4145, 0x70A4, 0x0000, 0x0000],
    2,
    MvkByteOrder.BigEndian);
// values[0] ≈ 12.34, values[1] = 0
```

### 7.9 Строки: `DecodeText`, `DecodeTextSwapped`, `EncodeTextSwapped`

`DecodeText` — прямой порядок (старший байт регистра — первый символ); используется
для серийного номера устройства:

```csharp
ushort[] serial = await device.ReadHoldingRegistersAsync(
    Mvk01RegisterMap.DeviceSerialNumber, 4, ct);
string text = Mvk01Codec.DecodeText(serial, 8);    // 8 байт = 4 регистра
```

`DecodeTextSwapped` обрабатывает строки каналов и тахометров: внутри каждого
4-байтного слова порядок байт обратный. Реальный пример — единицы измерения канала 1,
сырые регистры `32F1 2FEC`:

```csharp
ushort[] units = [0x32F1, 0x2FEC, 0x0000, 0x0000];
string text = Mvk01Codec.DecodeTextSwapped(units, 8);   // "м/с2"
```

Как это получается: байты регистров — `32 F1 2F EC`; swap внутри 4-байтного слова
даёт `EC 2F F1 32`; в cp1251 это `м`, `/`, `с`, `2`. Строка обрезается по первому
нулевому байту и тримится.

`EncodeTextSwapped` выполняет обратное преобразование для записи:

```csharp
ushort[] registers = Mvk01Codec.EncodeTextSwapped("м/с2", 4);
// [0x32F1, 0x2FEC, 0x0000, 0x0000]
```

Ограничения `EncodeTextSwapped`: `ArgumentNullException` при `text == null`,
`ArgumentOutOfRangeException` при `registerCount <= 0`, `ArgumentException`, если
закодированная строка не помещается в заданное число регистров.

`DefaultTextEncoding` — cp1251 (кодовая страница 1251); если провайдер кодовых страниц
недоступен, используется Latin1.

### 7.10 `FormatMd5`

```csharp
byte[] hash = [0x00, 0x11, 0xAB, 0xCD];
string md5 = Mvk01Codec.FormatMd5(hash);    // "0011abcd"
```

MD5-хэш DSP читается из `DspMd5` (8 регистров = 16 байт) и форматируется этим методом.

---

## 8. Mvk01Values — структуры значений

Все типы — из `src/Mvk.MVK01/Mvk01Values.cs`.

### `Mvk01FirmwareVersion` (readonly record struct)

| Поле | Тип | Смысл |
|---|---|---|
| `Major` | `int` | Версия |
| `Minor` | `int` | Подверсия 1 |
| `Patch` | `int` | Подверсия 2 |
| `Build` | `int` | Сборка |

`ToString()` возвращает `«1.7.20.0»`. Структура поддерживает сравнение через `==`.

### `Mvk01SystemErrors` (readonly record struct)

| Поле | Тип | Смысл |
|---|---|---|
| `DspReceiveError` | `bool` | Бит 0 |
| `SdCardError` | `bool` | Биты 1–3 |
| `DspSendError` | `bool` | Бит 4 |
| `ModbusInitializationError` | `bool` | Бит 5 |
| `ConfigurationCrcError` | `bool` | Бит 6 |
| `Raw` | `uint` | Исходное значение |
| `IsClean` | `bool` (свойство) | `Raw == 0` — ошибок нет |

### `Mvk01UnitState` (readonly record struct)

| Поле | Тип | Смысл |
|---|---|---|
| `Zone` | `Mvk01Zone` | Зона контроля (биты 0–7) |
| `SensorFailureEvent` | `bool` | Событие отказа датчика (бит 9) |
| `EnteredYellowZone` | `bool` | Переход в жёлтую зону (бит 10) |
| `EnteredRedZone` | `bool` | Переход в красную зону (бит 11) |
| `Raw` | `uint` | Исходное значение |

### `Mvk01DspIndicators` (readonly record struct)

| Поле/метод | Тип | Смысл |
|---|---|---|
| `OverloadChannelMask` | `byte` | Биты 0–7 — перегрузка каналов 1–8 |
| `ConfigurationFlags` | `uint` | Флаги ошибок конфигурации (биты 30/31) |
| `CalculationCounter` | `uint` | Счётчик выполненных процедур расчёта |
| `IsChannelOverloaded(channel)` | `bool` | Перегрузка канала 1–8; вне диапазона — исключение |

### `Mvk01ChannelState` (readonly record struct)

| Поле | Тип | Смысл |
|---|---|---|
| `Overload` | `bool` | Перегрузка входа (бит 0) |
| `Buffer16HzNotFilled` | `bool` | Буфер 16 Гц не заполнен (бит 4) |
| `Buffer64HzNotFilled` | `bool` | Буфер 64 Гц не заполнен (бит 5) |
| `Buffer512HzNotFilled` | `bool` | Буфер 512 Гц не заполнен (бит 6) |
| `DcVoltageAboveMaximum` | `bool` | Постоянное напряжение выше нормы (бит 10) |
| `DcVoltageBelowMinimum` | `bool` | Постоянное напряжение ниже нормы (бит 11) |
| `Raw` | `uint` | Исходное значение |

### `Mvk01RmsSet` (sealed record)

| Поле | Тип | Смысл |
|---|---|---|
| `Name` | `string` | Название полосы, например «10…3000 Гц» |
| `Acceleration` | `float` | СКЗ виброускорения, м/с2 |
| `Velocity` | `float` | СКЗ виброскорости, мм/с |
| `Displacement` | `float` | СКЗ виброперемещения, мкм |

---

## 9. Примеры

Во всех примерах предполагается, что `IMvkDevice device` уже получен
(из пула или через `Mvk01Connection`); `ct` — `CancellationToken`.

### 9.1 Декодировать версию ПО из сырых регистров

```csharp
using Mvk.Modbus.Protocol;
using Mvk.MVK01;

ushort[] registers = await device.ReadHoldingRegistersAsync(
    Mvk01RegisterMap.FirmwareVersion, 2, ct);        // 0x1004, чётный адрес и размер

uint packed = RegisterCodec.ToUInt32(registers, device.Settings.ByteOrder);
Mvk01FirmwareVersion version = Mvk01Codec.DecodeFirmwareVersion(packed);

Console.WriteLine($"ПО МВК01: {version}");           // например: ПО МВК01: 1.7.20.0
```

### 9.2 Прочитать состояние канала и разобрать биты

```csharp
using Mvk.Modbus.Protocol;
using Mvk.MVK01;

const int channel = 1;
ushort stateAddress = (ushort)(Mvk01RegisterMap.ChannelData(channel)
    + Mvk01RegisterMap.ChannelStateOffset);          // 0x2000 + 0x9E = 0x209E

ushort[] raw = await device.ReadHoldingRegistersAsync(stateAddress, 2, ct);
uint value = RegisterCodec.ToUInt32(raw, device.Settings.ByteOrder);
Mvk01ChannelState state = Mvk01Codec.DecodeChannelState(value);

if (state.Overload)
{
    Console.WriteLine("Перегрузка измерительного входа канала");
}

if (state.Buffer16HzNotFilled || state.Buffer64HzNotFilled || state.Buffer512HzNotFilled)
{
    Console.WriteLine("Буферы ещё не заполнены — часть фильтров не активна");
}

if (state.DcVoltageAboveMaximum)
{
    Console.WriteLine("Пост. напряжение выше нормы: вероятен обрыв кабеля датчика");
}

if (state.DcVoltageBelowMinimum)
{
    Console.WriteLine("Пост. напряжение ниже нормы: вероятно КЗ в кабеле датчика");
}
```

### 9.3 Собрать команду SaveConfiguration в регистры

```csharp
using Mvk.Modbus.Protocol;
using Mvk.MVK01;

// 1. Команда кодируется с учётом порядка байт устройства.
ushort[] command = Mvk01Codec.EncodeSystemCommand(
    Mvk01SystemCommand.SaveConfiguration, device.Settings.ByteOrder);
// BigEndian: [0x000F, 0x0001]

// 2. Запись двух регистров по адресу 0x0002.
ModbusRequest request = ModbusRequest.WriteMultipleRegisters(
    device.Settings.Id, Mvk01RegisterMap.SystemCommand, command);

// 3. Для SaveConfiguration документация требует таймаут до 3 с.
ModbusResponse response = await device.ExecuteAsync(
    request, TimeSpan.FromSeconds(3), ct);

if (!response.IsSuccess)
{
    throw response.Error ?? new InvalidOperationException("Команда не выполнена.");
}

// 4. Статус выполнения читается из того же регистра 0x0002.
ushort[] statusRegisters = await device.ReadHoldingRegistersAsync(
    Mvk01RegisterMap.SystemCommand, 2, ct);
uint status = RegisterCodec.ToUInt32(statusRegisters, device.Settings.ByteOrder);

switch (status)
{
    case Mvk01Codec.StatusSuccess:
        Console.WriteLine("Конфигурация сохранена во Flash");
        break;
    case Mvk01Codec.StatusBadCommand:
        Console.WriteLine("Устройство отклонило команду (BadCommand)");
        break;
    case Mvk01Codec.StatusFlashError:
        Console.WriteLine("Ошибка флэш-памяти, конфигурация не сохранена");
        break;
}
```

Запись в область `0x0000–0x0FFF` защищена политикой `ProtectConfigArea`; при
`AllowConfigurationWrite = false` запрос будет отклонён middleware `writeProtection`.
В `Mvk01Connection` запись разрешается параметром `allowConfigurationWrite: true`,
в DI — явной настройкой `Defaults.AllowConfigurationWrite = true`.
На реальном стенде Save/Reboot выполнять только с явного разрешения.

### 9.4 Декодировать набор СКЗ из регистров вручную

```csharp
using Mvk.Modbus.Protocol;
using Mvk.MVK01;

const int channel = 1;
const int setIndex = 2;                              // набор «10…1000 Гц»

ushort address = (ushort)(Mvk01RegisterMap.ChannelData(channel)
    + Mvk01RegisterMap.RmsSetOffset
    + setIndex * Mvk01RegisterMap.RmsSetValues * 2); // 0x2000 + 0x60 + 12 = 0x206C

ushort[] registers = await device.ReadHoldingRegistersAsync(address, 6, ct);

float[] set = Mvk01Codec.DecodeFloats(
    registers,
    Mvk01RegisterMap.RmsSetValues,                   // 3 значения
    device.Settings.ByteOrder);

Console.WriteLine($"10…1000 Гц: ускорение {set[0]:F3} м/с2, " +
                  $"скорость {set[1]:F3} мм/с, перемещение {set[2]:F1} мкм");
```

Тот же результат через типизированный слой — `Mvk01RmsSet` с именем полосы:
`channel.RmsSets[setIndex]` (см. [Фасад МВК01](07b-mvk01-device-facade.md)).

---

## 10. Особенности, проверенные на устройстве

Проверено на реальном МВК01 (10.20.0.211, ПО 1.0.67.1).

1. **Только чётные адрес и количество регистров.** Устройство принимает лишь запросы,
   у которых адрес первого регистра и число регистров чётные; иначе возвращается ошибка
   Modbus `0x01`. Для `Mvk01Connection` и DI это обеспечивает middleware `validation`
   (`RequireEvenAddressAndQuantity`). При ручном чтении адрес и количество нужно
   контролировать самостоятельно: например, `0x1004 + 2` — корректно, `0x1005 + 1` —
   ошибка устройства.
2. **Пример для N=48 в `MVK01_registers.txt` ошибочен.** В документе указан адрес
   `0x41F0`, фактически формула `0x4000 + (48−1)×16` даёт **`0x42F0`**. Проверено на
   устройстве: значение СКЗ совпало с прямым чтением канала. Библиотека использует
   формулу (тест `Mvk01RegisterMapTests` фиксирует `0x42F0`).
3. **Строки каналов — обратный порядок байт в 4-байтных словах и cp1251.**
   Единицы измерения (`A+0x10`), серийные номера датчиков каналов (`A+0x20`) и
   тахометров (`A+0x20`) декодируются `Mvk01Codec.DecodeTextSwapped`. Реальные
   регистры единиц канала 1: `32F1 2FEC` → «м/с2».
4. **Серийный номер устройства — прямой порядок.** `0x1000`, 8 байт (4 регистра),
   декодируется `Mvk01Codec.DecodeText`, а не `DecodeTextSwapped`.
5. **32-битные значения — два регистра**; порядок байт задаётся регистром `0x000C`
   (BigEndian по умолчанию). Все методы кодеков принимают `MvkByteOrder` из настроек
   устройства; жёстко зашитого порядка в коде нет.
6. **Копии для проверки согласованности.** В блоке канала есть
   `SensorVoltageCopy` (`A+0x9C`, параметр 79) и `ChannelStateCopy`
   (`A+0x120`, параметр 145). `ReadChannelSnapshotAsync` использует их, чтобы
   проверить, что три объединённых запроса не попали на момент обновления DSP.

---

## Подводные камни

- **Байты против регистров.** В документе длины — в байтах, адреса — в регистрах.
  «Единицы измерения: 32 байта» — это 16 регистров. Чтение 32 регистров «на всякий
  случай» вернёт лишние данные и может выйти за границу блока.
- **Смещения — не адреса.** `SensorVoltageOffset = 0x0000` и подобные константы
  заданы относительно базы канала/тахометра. Абсолютный адрес — всегда
  `ChannelData(n) + offset` (или `ChannelConfig(n)`, `TachoData(n)`).
- **Гармоники вне блока канала.** `HarmonicsPhaseOffset`/`HarmonicsAmplitudeOffset`
  (`+0x7034`/`+0x703C`) дают адреса в области `0x9000+`, а не внутри
  `0x2000–0x2FFF`. Не пытайтесь прочитать их как часть 512-регистрового снимка канала —
  для этого нужен отдельный запрос.
- **Формулы не валидируют аргументы.** `ChannelConfig(0)`, `ChannelData(9)`,
  `MeasurementByNumber(200)` не бросят исключение, а вернут адрес из чужого блока.
  Диапазоны: каналы 1–8, тахометры 1–2, номера 1–184.
- **Два разных СКЗ-пути.** `RmsX25kHzOffset` (`+0x5E`, параметр 48) — отдельное
  значение, а `RmsSetOffset` (`+0x60`) — начало восьми наборов (параметры 49–72).
  `RmsSets[0]` — это 10…3000 Гц, а не X…25 кГц.
- **Не писать резервный коэффициент.** `ChannelConversionFactorOffset` (`+0x0E`) —
  «не писать, считается устройством»; запись туда ломает масштабирование канала.
- **Чётность обязательна.** Любой нечётный адрес или размер — ошибка `0x01` от
  устройства. Блоки в карте чётные, но при вычислении адреса вручную это легко
  нарушить (например, `+0x9E` — чётно, а `+0x9D` — уже нет).
- **Лимит 125 регистров на запрос.** Блок канала — 512 регистров, спектр — 86;
  большие объёмы читаются чанками. В `Mvk01Connection`/`IMvk01Device` это делается
  автоматически, при ручном чтении — на вашей стороне.
- **Порядок байт.** Всегда передавайте `device.Settings.ByteOrder`; для ВДМ82
  устройство может быть сконфигурировано на `Mixed`. Ручной `BitConverter` без учёта
  порядка — типичная ошибка.
- **`DecodeText` против `DecodeTextSwapped`.** Перепутать их — получить «мусорные»
  символы (например, «2с/м» вместо «м/с2»). Прямой порядок — только серийный номер
  устройства (`0x1000`); всё, что лежит в блоках каналов и тахометров, — swap.
- **cp1251.** `DefaultTextEncoding` регистрирует провайдер кодовых страниц; без
  пакета `System.Text.Encoding.CodePages` (зависимость `Mvk.MVK01`) строки уходят
  в Latin1 и русские единицы измерения декодируются неверно.
- **Культура в `FormatBand`.** Форматирование `0.###` использует текущую культуру:
  в русской — «0,63 Гц», в инвариантной — «0.63 Гц». Для парсинга/экспорта
  подписей это важно.
- **`Mvk01CommandStatus.Pending/Timeout/TransportError`** — отрицательные значения
  (−1, −2, −3), а не биты регистра. Они означают не статус устройства, а результат
  ожидания статуса библиотекой; не сравнивайте их с сырым значением регистра `0x0002`.
- **Тип канала тахометра** (`A+0x00`) и конфигурация DSP (`0x0000`) не вынесены
  в константы `Mvk01RegisterMap`; в документе они помечены как «= 0» и «не писать».
- **Резервные номера параметров** (105–112, 137–144, 146–152, а также 45, 46, 73–77
  в блоках каналов) адресуются формулой, но полезных данных не содержат; номера
  113–122 в таблице 5.1.1 не описаны вовсе.

---

## См. также

- [Фасад МВК01](07b-mvk01-device-facade.md) — типизированные вызовы поверх карты регистров и кодеков.
- [Транспорт](04-transport-and-raw-registers.md) — подключение, чтение/запись сырых регистров, `RegisterCodec`.
- [README](../README.md) — обзор библиотеки, быстрый старт и настройка.
