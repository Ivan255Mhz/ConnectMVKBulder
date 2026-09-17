# Глава 04. Транспорт и сырые регистры

Низкоуровневый слой библиотеки: соединение с одним МВК01 и обмен «сырыми»
16-битными регистрами без типизированного фасада `Mvk.MVK01`.
Глава описывает `IMvkTransport`/`NModbusTransport`, устройство пула `IMvkDevice`,
запросы `ModbusRequest`, ответы `ModbusResponse`, операции `ModbusOperation`
и кодек `RegisterCodec`, а также практические примеры и диагностику ошибок связи.

Содержание:

1. [Обзор уровня](#1-обзор-уровня)
2. [Транспорт: IMvkTransport и NModbusTransport](#2-транспорт-imvktransport-и-nmodbustransport)
3. [Устройство пула: IMvkDevice](#3-устройство-пула-imvkdevice)
4. [ModbusRequest](#4-modbusrequest)
5. [ModbusResponse](#5-modbusresponse)
6. [ModbusOperation](#6-modbusoperation)
7. [RegisterCodec](#7-registercodec)
8. [Практические примеры](#8-практические-примеры)
9. [Диагностика](#9-диагностика)
10. [Особенность МВК01: чётные адрес и количество регистров](#10-особенность-мвк01-чётные-адрес-и-количество-регистров)
11. [Подводные камни](#11-подводные-камни)
12. [См. также](#12-см-также)

---

## 1. Обзор уровня

Путь одного запроса от приложения до сокета:

```
Приложение
  └─ IMvkDevicePool / IMvkDevice        — пул: состояния, реконнект, сериализация, таймауты
       └─ MvkPipeline (middleware)      — проверки, ретраи, метрики, защита записи
            └─ IMvkTransport            — транспорт одного устройства
                 └─ NModbusTransport    — TcpClient + master NModbus (Modbus/TCP)
```

| Слой | Тип | Зона ответственности |
|---|---|---|
| Транспорт | `IMvkTransport`, `NModbusTransport` | Сокет, ленивое подключение, таймауты, разрыв при ошибке, коды функций Modbus |
| Пайплайн | `MvkPipeline`, `IModbusMiddleware` | Проверки запроса, повторы временных ошибок, общий таймаут, метрики, политика записи |
| Пул | `IMvkDevice`, `MvkDevice`, `IMvkDevicePool` | Очередь запросов, состояния `Disconnected/Connecting/Connected/Faulted`, реконнект, простой |
| Протокол | `ModbusRequest`, `ModbusResponse`, `ModbusOperation`, `RegisterCodec` | Модель запроса/ответа и преобразование регистров в значения |

Сырой API нужен, когда:

- адрес или параметр ещё не описан типизированным слоем (новые версии ПО, эксперименты);
- нужно прочитать «как есть» нестандартный диапазон регистров (диагностика, отладка обмена);
- нужен полный контроль над транспортом: свои таймауты, ручное подключение,
  собственный пайплайн или своя фабрика транспортов;
- нужно декодировать значение вручную (`RegisterCodec`), например битовое поле состояния.

Два уровня доступа к сырым регистрам:

| Способ | Как получить | Что даёт |
|---|---|---|
| `IMvkDevice` из пула (рекомендуется) | `pool.GetDevice("mvk-1")` | Пайплайн, состояния, реконнект, обёртки чтения/записи |
| `NModbusTransport` напрямую | `new NModbusTransport(settings)` | Только транспорт: ни проверок, ни ретраев, ни реконнекта — всё вручную |

Пространства имён: `Mvk.Modbus`, `Mvk.Modbus.Configuration`, `Mvk.Modbus.Pool`,
`Mvk.Modbus.Protocol`, `Mvk.Modbus.Transport`.

## 2. Транспорт: IMvkTransport и NModbusTransport

### `IMvkTransport`

| Член | Сигнатура | Описание |
|---|---|---|
| `IsConnected` | `bool` (get) | Соединение установлено и сокет открыт |
| `ConnectAsync` | `Task ConnectAsync(CancellationToken ct)` | Явное подключение; при ошибке — `MvkModbusException` (`Transport`/`Timeout`) |
| `DisconnectAsync` | `Task DisconnectAsync()` | Закрытие соединения (синхронное по сути) |
| `ExecuteAsync` | `Task<ModbusResponse> ExecuteAsync(ModbusRequest request, CancellationToken ct)` | Выполняет запрос, при необходимости подключаясь; транспортные ошибки — в `ModbusResponse.Failure(...)` |
| `Dispose` | `void Dispose()` | Закрывает соединение и освобождает ресурсы |

Контракт `ExecuteAsync`:

- отмена вызывающего кода (`CancellationToken`) выбрасывает `OperationCanceledException`,
  а не возвращает `Failure`;
- ошибки транспорта/устройства возвращаются как `ModbusResponse` с `Error`,
  чтобы вызывающий код сам решал, повторять запрос или нет;
- после `Dispose` — `ObjectDisposedException`; `null`-запрос — `ArgumentNullException`.

### `NModbusTransport`

```csharp
public sealed class NModbusTransport : IMvkTransport
{
    public NModbusTransport(MvkDeviceSettings settings, ILogger<NModbusTransport>? logger = null);
}
```

- `settings` обязателен (`ArgumentNullException`); логгер необязателен
  (по умолчанию `NullLogger`).
- Экземпляр предназначен для одного устройства; создаётся фабрикой пула.
- Потокобезопасен: подключение защищено `SemaphoreSlim`, параллельные вызовы
  `ConnectAsync` не создают второй сокет.

Поведение:

| Ситуация | Что делает транспорт |
|---|---|
| Первый `ExecuteAsync` без соединения | Лениво вызывает `ConnectAsync` и выполняет запрос |
| `ConnectAsync` при живом соединении | Ничего не делает (идемпотентен) |
| Таймаут подключения | `MvkModbusException` (`Timeout`): «Таймаут подключения к host:port (N мс)» |
| Таймаут ответа на чтение | `master.Transport.ReadTimeout = settings.ReadTimeoutMs` |
| Таймаут ответа на запись | `master.Transport.WriteTimeout = settings.WriteTimeoutMs` |
| Любая ошибка выполнения | `CloseConnection()`: master и `TcpClient` освобождаются, `IsConnected` = false |
| Следующий `ExecuteAsync` после ошибки | Снова подключается (авто-разрыв + ленивый реконнект) |
| Отмена вызывающим кодом | `OperationCanceledException` без закрытия соединения |
| Неподдерживаемая операция | `ModbusResponse.Failure` с `MvkErrorKind.Validation` |

Таймауты берутся только из `MvkDeviceSettings`: `ConnectTimeoutMs`, `ReadTimeoutMs`,
`WriteTimeoutMs`. Общий таймаут операции (`ExecuteAsync(request, timeout, ct)`)
живёт на уровне устройства и пайплайна, см. раздел 3.

Преобразование исключений (`MapException`) — просматриваются и вложенные исключения:

| Исключение | `MvkErrorKind` | `ModbusExceptionCode` |
|---|---|---|
| `TimeoutException` | `Timeout` | — |
| `SocketException` с `SocketError.TimedOut` | `Timeout` | — |
| `IOException` со словом `timeout`/`timed out` | `Timeout` | — |
| `SlaveException` (ответ-исключение устройства) | `DeviceError` | код из ответа |
| Прочие | `Transport` | — |

### Фабрика транспортов

| Тип | Сигнатура | Описание |
|---|---|---|
| `IMvkTransportFactory` | `IMvkTransport Create(MvkDeviceSettings settings)` | По одному транспорту на устройство |
| `NModbusTransportFactory` | `NModbusTransportFactory(ILoggerFactory? loggerFactory = null)` | Создаёт `NModbusTransport` |

В DI `AddMvkModbus` регистрирует `IMvkTransportFactory` как singleton
(`TryAddSingleton`); своя регистрация после `AddMvkModbus` заменяет стандартную.

Ручное использование транспорта без пула:

```csharp
using Mvk.Modbus;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Protocol;
using Mvk.Modbus.Transport;

MvkDeviceSettings settings = new MvkDeviceOptions
{
    Id = "mvk-1",
    Host = "10.20.0.211"
}.Resolve(new MvkDefaultsOptions());

using NModbusTransport transport = new(settings);

ModbusResponse response = await transport.ExecuteAsync(
    ModbusRequest.ReadHoldingRegisters(settings.Id, 0x1004, 2),
    CancellationToken.None);

if (response.IsSuccess)
{
    Console.WriteLine($"Версия (сырые регистры): 0x{response.Data[0]:X4} 0x{response.Data[1]:X4}");
}
else
{
    Console.WriteLine($"{response.Error!.ErrorKind}: {response.Error.Message}");
}
```

Важно: такой транспорт не знает о пайплайне — проверки чётности, ретраи,
защита записи и метрики не работают. Всё это доступно через `IMvkDevice` (раздел 3).

## 3. Устройство пула: IMvkDevice

`MvkDevice` создаётся пулом из `MvkDeviceSettings`, транспорта и пайплайна;
прикладной код получает его как `IMvkDevice` (см. главу [Пул устройств](05-device-pool.md)).

| Член | Сигнатура | Описание |
|---|---|---|
| `Settings` | `MvkDeviceSettings` | Параметры с применёнными `Defaults` (включая `Endpoint`) |
| `State` | `MvkDeviceState` | Текущее состояние: `Disconnected`, `Connecting`, `Connected`, `Faulted` |
| `IsConnected` | `bool` | Признак живого соединения (делегируется транспорту) |
| `LastError` | `MvkModbusException?` | Последняя ошибка; сбрасывается при успешном запросе |
| `LastRequestAt` | `DateTimeOffset` | Время последнего запроса (UTC); `DateTimeOffset.MinValue` — запросов не было |
| `StateChanged` | `event EventHandler<MvkDeviceStateChangedEventArgs>?` | Переходы состояния с ошибкой-причиной |
| `ExecuteAsync` | `Task<ModbusResponse> ExecuteAsync(ModbusRequest request, CancellationToken ct = default)` | Запрос через пайплайн |
| `ExecuteAsync` | `Task<ModbusResponse> ExecuteAsync(ModbusRequest request, TimeSpan timeout, CancellationToken ct = default)` | То же с общим таймаутом операции |
| `EnsureConnectedAsync` | `Task EnsureConnectedAsync(CancellationToken ct = default)` | Явно подключиться (проверка доступности) |
| `DisconnectAsync` | `Task DisconnectAsync(CancellationToken ct = default)` | Закрыть соединение и перейти в `Disconnected` |
| `ReadHoldingRegistersAsync` | `Task<ushort[]> ReadHoldingRegistersAsync(ushort address, ushort quantity, CancellationToken ct = default)` | Чтение 0x03 |
| `ReadInputRegistersAsync` | `Task<ushort[]> ReadInputRegistersAsync(ushort address, ushort quantity, CancellationToken ct = default)` | Чтение 0x04 |
| `WriteSingleRegisterAsync` | `Task WriteSingleRegisterAsync(ushort address, ushort value, CancellationToken ct = default)` | Запись 0x06 |
| `WriteMultipleRegistersAsync` | `Task WriteMultipleRegistersAsync(ushort address, ushort[] data, CancellationToken ct = default)` | Запись 0x10 |
| `Dispose` | `void Dispose()` | Освобождает транспорт и очередь запросов |

Контракт:

- `ExecuteAsync` **не выбрасывает** `MvkModbusException`: и ошибка устройства,
  и ошибка связи, и отключённое устройство приходят как `ModbusResponse.Error`.
  Исключения — только `OperationCanceledException` (отмена) и
  `ObjectDisposedException` (после `Dispose`). Исключение по таймауту операции
  middleware превращает в ответ с `MvkErrorKind.Timeout`.
- Обёртки `ReadHoldingRegistersAsync`, `ReadInputRegistersAsync`,
  `WriteSingleRegisterAsync`, `WriteMultipleRegistersAsync` при неуспехе
  **выбрасывают** `response.Error!` (`MvkModbusException`), при успехе чтения
  возвращают `response.Data`.
- `EnsureConnectedAsync` при недоступности выбрасывает `MvkModbusException`
  (в отличие от `ExecuteAsync`), при успехе переводит состояние в `Connected`.
- `Enabled = false` в параметрах устройства: `ExecuteAsync` немедленно возвращает
  `Failure` с `MvkErrorKind.DeviceDisabled` (сообщение: «Устройство 'mvk-1' отключено
  в конфигурации (Enabled = false).»), `EnsureConnectedAsync` — выбрасывает такую же ошибку.
- Запросы к одному устройству сериализуются семафором на `MaxConcurrentRequests`
  параллельных вызовов.
- `LastRequestAt` обновляется в начале запроса (после постановки в очередь),
  `LastError` — по результату: успех очищает ошибку, временная ошибка переводит
  устройство в `Faulted` и планирует реконнект, невременная (например, `DeviceError`)
  записывается в `LastError` без `Faulted` — следующее обращение просто подключится
  заново. Причина в том, что транспорт закрывает соединение при любой ошибке запроса.

Пример: оба стиля работы с устройством.

```csharp
using Mvk.Modbus;
using Mvk.Modbus.Protocol;

IMvkDevice device = pool.GetDevice("mvk-1");

// 1. Без исключений: результат проверяется вручную.
ModbusResponse response = await device.ExecuteAsync(
    ModbusRequest.ReadHoldingRegisters("mvk-1", 0x1004, 2));

if (response.IsSuccess)
{
    Console.WriteLine($"Регистры версии: 0x{response.Data[0]:X4} 0x{response.Data[1]:X4}");
}
else
{
    Console.WriteLine($"Ошибка: {response.Error!.ErrorKind} — {response.Error.Message}");
}

// 2. С исключением: удобно в «счастливом пути».
ushort[] registers = await device.ReadHoldingRegistersAsync(0x1004, 2);

// 3. Общий таймаут операции на один вызов (middleware timeout).
response = await device.ExecuteAsync(
    ModbusRequest.ReadHoldingRegisters("mvk-1", 0x2002, 86),
    TimeSpan.FromSeconds(3));
```

Событие состояния:

```csharp
device.StateChanged += (_, args) =>
{
    Console.WriteLine($"{args.Device.Settings.Id}: {args.OldState} -> {args.NewState}");
    if (args.Error is not null)
    {
        Console.WriteLine($"  причина: {args.Error.ErrorKind} — {args.Error.Message}");
    }
};

await device.EnsureConnectedAsync();   // Disconnected -> Connecting -> Connected
```

## 4. ModbusRequest

Неизменяемый запрос. Создаётся только фабриками (конструктор приватный).

| Член | Тип | Описание |
|---|---|---|
| `DeviceId` | `string` | Идентификатор устройства (пробелы по краям обрезаются) |
| `Operation` | `ModbusOperation` | Операция (см. раздел 6) |
| `StartAddress` | `ushort` | Адрес первого регистра |
| `Quantity` | `ushort` | Число регистров |
| `WriteData` | `ushort[]?` | Данные записи; `null` для чтения |
| `IsWrite` | `bool` | `true` для 0x06 и 0x10 |
| `EndAddress` | `int` | Последний затрагиваемый регистр включительно: `StartAddress + Quantity - 1` |

Константы:

| Константа | Значение | Ограничение |
|---|---|---|
| `ModbusRequest.MaxReadRegisters` | 125 | Максимум регистров в одном запросе чтения |
| `ModbusRequest.MaxWriteRegisters` | 123 | Максимум регистров в одном запросе записи |

Фабрики:

```csharp
public static ModbusRequest ReadHoldingRegisters(string deviceId, ushort startAddress, ushort quantity);
public static ModbusRequest ReadInputRegisters(string deviceId, ushort startAddress, ushort quantity);
public static ModbusRequest WriteSingleRegister(string deviceId, ushort address, ushort value);
public static ModbusRequest WriteMultipleRegisters(string deviceId, ushort startAddress, ushort[] data);
```

Ошибки аргументов:

| Условие | Исключение |
|---|---|
| `deviceId` пустой или из пробелов | `ArgumentException` (в любой фабрике) |
| `data` для `WriteMultipleRegisters` — `null` | `ArgumentNullException` |
| `data.Length` = 0 или > 123 | `ArgumentException` |
| `quantity` = 0 или больше предела (125 чтение / 123 запись) | `ArgumentOutOfRangeException` |
| `StartAddress + Quantity` выходит за 65536 | `ArgumentOutOfRangeException` |
| `WriteData.Length` не совпадает с `Quantity` | `ArgumentException` (внутренняя проверка конструктора) |

Особенности:

- массив `data` сохраняется по ссылке — не изменяйте его после создания запроса;
- `DeviceId` обрезается по краям: `"  mvk-1  "` → `"mvk-1"`;
- запрос не привязан к конкретному экземпляру устройства: `DeviceId` — только метка
  для логов и контекста пайплайна.

## 5. ModbusResponse

Результат выполнения запроса — данные либо ошибка.

| Член | Тип | Описание |
|---|---|---|
| `IsSuccess` | `bool` | `true` — запрос выполнен |
| `Data` | `ushort[]` | Данные чтения; для записи и ошибок — пустой массив (не `null`) |
| `Error` | `MvkModbusException?` | Ошибка; `null` при успехе |

Фабрики:

| Метод | Назначение |
|---|---|
| `Success(ushort[] data)` | Успех с данными; `data` — `null` → `ArgumentNullException` |
| `WriteSuccess()` | Успех записи: `IsSuccess = true`, `Data` — пустой массив |
| `Failure(MvkModbusException error)` | Ошибка; `error` — `null` → `ArgumentNullException` |
| `Failure(Exception exception, string? deviceId = null)` | Ошибка из произвольного исключения |

Правила `Failure(Exception, string?)`:

- если исключение уже `MvkModbusException` — оно сохраняется как есть
  (`Error` — тот же экземпляр);
- иначе оборачивается в `MvkModbusException` с `MvkErrorKind.Transport`
  и указанным `deviceId`; исходное исключение попадает в `InnerException`.

Проверка ответа — всегда через `IsSuccess`, `Data` при ошибке не заполняется,
`Error` при успехе равен `null`.

## 6. ModbusOperation

| Значение | Код функции | Смысл |
|---|---|---|
| `ReadHoldingRegisters` | 0x03 | Чтение регистров хранения |
| `ReadInputRegisters` | 0x04 | Чтение входных регистров |
| `WriteSingleRegister` | 0x06 | Запись одного регистра хранения |
| `WriteMultipleRegisters` | 0x10 | Запись нескольких регистров хранения |

На МВК01 чтение данных выполняется функцией 0x03, запись — 0x06/0x10.
Транспорт `NModbusTransport` поддерживает ровно эти четыре операции; любая другая
(значение, полученное нештатным путём) вернёт `Failure` с `MvkErrorKind.Validation`.

## 7. RegisterCodec

`Mvk.Modbus.Protocol.RegisterCodec` — статический класс преобразования регистров
в значения и обратно. Порядок байт берётся из `device.Settings.ByteOrder`
(регистр 0x000C МВК01).

| Метод | Сигнатура | Назначение |
|---|---|---|
| `ToUInt32` | `uint ToUInt32(ReadOnlySpan<ushort> registers, MvkByteOrder byteOrder)` | uint32 из двух регистров |
| `ToInt32` | `int ToInt32(ReadOnlySpan<ushort> registers, MvkByteOrder byteOrder)` | int32 из двух регистров |
| `ToSingle` | `float ToSingle(ReadOnlySpan<ushort> registers, MvkByteOrder byteOrder)` | float из двух регистров |
| `FromUInt32` | `ushort[] FromUInt32(uint value, MvkByteOrder byteOrder)` | uint32 → два регистра |
| `FromSingle` | `ushort[] FromSingle(float value, MvkByteOrder byteOrder)` | float → два регистра |
| `ToBytes` | `byte[] ToBytes(ReadOnlySpan<ushort> registers, int byteCount = -1)` | Регистры → байты (старший байт регистра первый) |
| `ToText` | `string ToText(ReadOnlySpan<ushort> registers, Encoding encoding, int byteCount = -1)` | Регистры → строка |
| `FromText` | `ushort[] FromText(string text, int registerCount, Encoding encoding)` | Строка → регистры |
| `GetBit` | `bool GetBit(ushort value, int bitIndex)` | Бит 0–15 |
| `GetBit` | `bool GetBit(uint value, int bitIndex)` | Бит 0–31 |

Порядок байт `MvkByteOrder`:

| Значение | Смысл | Регистр 0x000C |
|---|---|---|
| `BigEndian` | Старшее слово первым (по умолчанию) | 0x00000000 |
| `LittleEndian` | Полный обратный порядок байт | 0x66666666 |
| `Mixed` | Слова меняются местами (для ВДМ82) | 0x99999999 |

Эталонные примеры (совпадают с тестами `RegisterCodecTests`):

| Значение | BigEndian | Mixed | LittleEndian |
|---|---|---|---|
| `1.0f` | 0x3F80, 0x0000 | 0x0000, 0x3F80 | 0x0000, 0x803F |
| `12.34f` | 0x4145, 0x70A4 | 0x70A4, 0x4145 | 0xA470, 0x4541 |
| `0x000F0001u` | 0x000F, 0x0001 | 0x0001, 0x000F | 0x0100, 0x0F00 |

Декодирование:

```csharp
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Protocol;

// Два регистра из ответа устройства.
ushort first = registers[0];
ushort second = registers[1];

float value = RegisterCodec.ToSingle(registers, device.Settings.ByteOrder);
uint raw = RegisterCodec.ToUInt32(registers, device.Settings.ByteOrder);
int signed = RegisterCodec.ToInt32(registers, device.Settings.ByteOrder);
```

Проверки и исключения:

| Метод | Условие | Исключение |
|---|---|---|
| `ToUInt32`/`ToInt32`/`ToSingle` | Меньше двух регистров | `ArgumentException` |
| Все методы с `byteOrder` | Неизвестное значение перечисления | `ArgumentOutOfRangeException` |
| `FromText` | `registerCount <= 0` | `ArgumentOutOfRangeException` |
| `FromText` | Строка не помещается в `registerCount` регистров | `ArgumentException` |
| `GetBit(ushort, ...)` | `bitIndex` вне 0–15 | `ArgumentOutOfRangeException` |
| `GetBit(uint, ...)` | `bitIndex` вне 0–31 | `ArgumentOutOfRangeException` |

`ToInt32` — знаковая интерпретация тех же бит (`unchecked((int)ToUInt32(...))`):
например, `0xFFFFFFFE` → `-2`.

Кодирование для записи:

```csharp
float sensorFactor = 1.02f;

ushort[] payload = RegisterCodec.FromSingle(sensorFactor, device.Settings.ByteOrder);
// payload.Length == 2

// Круговой обмен без потерь:
float restored = RegisterCodec.ToSingle(payload, device.Settings.ByteOrder);
```

Байты и строки:

```csharp
using System.Text;

// Старший байт регистра — первый в массиве.
byte[] bytes = RegisterCodec.ToBytes(registers);
byte[] firstFour = RegisterCodec.ToBytes(registers, 4);   // ограничение длины

// Строка обрезается по первому нулевому байту, пробелы по краям удаляются.
string units = RegisterCodec.ToText(registers, Encoding.ASCII);
string unitsCp1251 = RegisterCodec.ToText(registers, Encoding.GetEncoding(1251), byteCount: 32);

// Обратное преобразование: свободные байты заполняются нулями.
ushort[] textRegisters = RegisterCodec.FromText("m/s2", 16, Encoding.ASCII);
```

Биты:

```csharp
ushort state = registers[0];

bool overload = RegisterCodec.GetBit(state, 0);
bool dcVoltageAboveMaximum = RegisterCodec.GetBit(state, 10);

uint indicators = RegisterCodec.ToUInt32(registers, device.Settings.ByteOrder);
bool sensorFailure = RegisterCodec.GetBit(indicators, 9);
```

## 8. Практические примеры

Контекст всех примеров: `IMvkDevice device = pool.GetDevice("mvk-1");`
(создание пула — см. главу [Пул устройств](05-device-pool.md)).
Для МВК01 устройство должно иметь `RequireEvenAddressAndQuantity = true`
(это делает `AddMvk01Devices()`; при ручной конфигурации включите параметр сами —
см. раздел 10).

### 8.1. Проверить связь и версию ПО

Версия ПО — регистр 0x1004 (uint32, два регистра). Упакованное значение
`0x01071400` соответствует версии `1.7.20.0`.

```csharp
using Mvk.Modbus;
using Mvk.Modbus.Protocol;

try
{
    await device.EnsureConnectedAsync(ct);   // связь есть, иначе MvkModbusException
    Console.WriteLine($"Связь: {device.State}, {device.Settings.Endpoint}");
}
catch (MvkModbusException ex)
{
    Console.WriteLine($"Устройство недоступно: {ex.ErrorKind} — {ex.Message}");
    return;
}

ushort[] versionRegisters = await device.ReadHoldingRegistersAsync(0x1004, 2, ct);
uint packed = RegisterCodec.ToUInt32(versionRegisters, device.Settings.ByteOrder);

string version = $"{(packed >> 24) & 0xFF}.{(packed >> 16) & 0xFF}."
               + $"{(packed >> 8) & 0xFF}.{packed & 0xFF}";

Console.WriteLine($"Версия ПО: {version}");
```

### 8.2. Прочитать напряжение канала

Блок расчётных параметров канала: канал 1 — 0x2000, шаг 0x200.
Смещение напряжения на датчике — 0x0000, значение float (2 регистра).

```csharp
const int channel = 1;
ushort channelBase = (ushort)(0x2000 + (channel - 1) * 0x200);
ushort voltageAddress = channelBase;   // sensorVoltage = base + 0x0000

ushort[] voltageRegisters = await device.ReadHoldingRegistersAsync(voltageAddress, 2, ct);
float sensorVoltage = RegisterCodec.ToSingle(voltageRegisters, device.Settings.ByteOrder);

Console.WriteLine($"Канал {channel}: напряжение {sensorVoltage:F4} В");
```

### 8.3. Прочитать 86 регистров спектра

Дольоктавный спектр — 43 полосы × 2 регистра = 86 регистров
по смещению 0x0002 от базы канала. 86 — чётное число, адрес 0x2002 чётный:
запрос проходит ограничение МВК01.

```csharp
const int bands = 43;

ushort spectrumAddress = (ushort)(0x2000 + 0x0002);   // канал 1: 0x2002
ushort[] spectrumRegisters = await device.ReadHoldingRegistersAsync(spectrumAddress, 86, ct);

float[] spectrum = new float[bands];
for (int band = 0; band < bands; band++)
{
    spectrum[band] = RegisterCodec.ToSingle(
        spectrumRegisters.AsSpan(band * 2, 2),
        device.Settings.ByteOrder);
}

for (int band = 0; band < bands; band++)
{
    Console.WriteLine($"Полоса {band + 1}: {spectrum[band]:F4}");
}
```

### 8.4. Записать один регистр

Функция 0x06, один регистр. Пример — таймаут неактивности клиента (0x0018, секунды).

```csharp
ushort clientTimeoutAddress = 0x0018;
ushort clientTimeoutSeconds = 30;

await device.WriteSingleRegisterAsync(clientTimeoutAddress, clientTimeoutSeconds, ct);
```

Учтите два ограничения: запись в защищённую область 0x0000–0x0FFF при
`WritePolicy = ProtectConfigArea` и `AllowConfigurationWrite = false` будет
отклонена пайплайном (`MvkErrorKind.Validation`), а на МВК01 при включённой
проверке чётности одиночная запись (`Quantity = 1`) не пройдёт проверку —
для МВК01 используйте пакетную запись (см. 8.5).

### 8.5. Пакетная запись нескольких регистров

Функция 0x10. Пишем два float-параметра канала 1 одним запросом:
коэффициент преобразования датчика (смещение 0x0002) и порог перегрузки
(смещение 0x0004) — итого 4 регистра, адрес 0x0102 чётный.

```csharp
ushort configAddress = (ushort)(0x0100 + 0x0002);   // канал 1: 0x0102

ushort[] payload =
[
    .. RegisterCodec.FromSingle(1.02f, device.Settings.ByteOrder),   // коэффициент
    .. RegisterCodec.FromSingle(2.5f, device.Settings.ByteOrder)     // порог перегрузки
];

await device.WriteMultipleRegistersAsync(configAddress, payload, ct);
```

Проверка результата без исключений:

```csharp
ModbusResponse response = await device.ExecuteAsync(
    ModbusRequest.WriteMultipleRegisters("mvk-1", configAddress, payload));

if (!response.IsSuccess)
{
    Console.WriteLine($"Запись не выполнена: {response.Error!.ErrorKind}");
}
```

### 8.6. Прочитать параметр по номеру вручную

Адресация расчётных параметров по номерам: `0x4000 + (N−1)×16`, один параметр —
16 регистров = 8 каналов по 2 регистра (float). Для `N = 48`
(СКЗ виброускорения X…25 кГц): `0x4000 + 47×16 = 0x42F0`.

```csharp
const int measurementNumber = 48;   // СКЗ виброускорения X...25 кГц
const int channelCount = 8;

ushort measurementAddress = (ushort)(0x4000 + (measurementNumber - 1) * 16);   // 0x42F0

ushort[] measurementRegisters = await device.ReadHoldingRegistersAsync(measurementAddress, 16, ct);

float[] byChannel = new float[channelCount];
for (int index = 0; index < channelCount; index++)
{
    byChannel[index] = RegisterCodec.ToSingle(
        measurementRegisters.AsSpan(index * 2, 2),
        device.Settings.ByteOrder);
}

for (int index = 0; index < channelCount; index++)
{
    Console.WriteLine($"Канал {index + 1}: {byChannel[index]:F4}");
}
```

## 9. Диагностика

### Состояние и последняя ошибка

| Признак | Что означает |
|---|---|
| `device.State == Faulted` | Транспортная ошибка/таймаут: соединение закрыто, запланирован реконнект |
| `device.IsConnected == false` | Сокет закрыт (после ошибки или `IdleDisconnectSeconds`) |
| `device.LastError.ErrorKind` | `Transport`/`Timeout` — связь; `DeviceError` — ответ-исключение; `Validation` — проверки; `DeviceDisabled` — `Enabled=false` |
| `device.LastError.IsTransient == true` | Ошибку имеет смысл повторить (`Transport`, `Timeout`, `Busy`) |
| `device.LastRequestAt` | Время последнего запроса (UTC); давно не обновлялось — опрос не идёт |
| `device.StateChanged` | Переход с `args.OldState`, `args.NewState`, `args.Error` |

Успешный запрос сбрасывает `LastError`; невременная ошибка (например,
`DeviceError` от устройства) записывается в `LastError`, но соединение не рвёт.

### Как выглядят ошибки

| Ситуация | `ErrorKind` | Текст сообщения (пример) |
|---|---|---|
| Нет соединения при подключении | `Transport` | `Не удалось подключиться к 10.20.0.211:502: <сообщение сокета>` |
| Таймаут подключения | `Timeout` | `Таймаут подключения к 10.20.0.211:502 (3000 мс).` |
| Нет ответа на запрос | `Timeout` | `Таймаут при обращении к 10.20.0.211:502.` |
| Общий таймаут операции | `Timeout` | `Таймаут операции ReadHoldingRegisters (0x2002) — превышено 1000 мс.` |
| Сбой ввода-вывода | `Transport` | `Ошибка при обращении к 10.20.0.211:502: <сообщение>` |
| Ответ-исключение устройства | `DeviceError` | `Устройство 'mvk-1' вернуло ошибку Modbus 0x01 (функция 0x83).` |
| Устройство выключено | `DeviceDisabled` | `Устройство 'mvk-1' отключено в конфигурации (Enabled = false).` |
| Не пройдена проверка запроса | `Validation` | `Адрес 0x2001 и число регистров 1 должны быть чётными (особенность устройства 'mvk-1').` |
| Нарушена политика записи | `Validation` | `Запись в защищённую область конфигурации (0x0000–0x0FFF) устройства 'mvk-1' запрещена. Разрешите AllowConfigurationWrite.` |

Полный вывод с разбором:

```csharp
ModbusResponse response = await device.ExecuteAsync(
    ModbusRequest.ReadHoldingRegisters("mvk-1", 0x1004, 2));

if (!response.IsSuccess)
{
    MvkModbusException error = response.Error!;

    Console.WriteLine($"Состояние: {device.State}, соединение: {device.IsConnected}");
    Console.WriteLine($"Ошибка: {error.ErrorKind} (временная: {error.IsTransient})");
    Console.WriteLine($"Текст: {error.Message}");

    if (error.ModbusExceptionCode is byte code)
    {
        // Код из ответа-исключения Modbus: 0x01, 0x02, 0x03 и т.д.
        Console.WriteLine($"Код Modbus: 0x{code:X2}");
    }
}
```

### Чтение кода ошибки Modbus

`ModbusExceptionCode` заполняется только для `MvkErrorKind.DeviceError`
(исключение `SlaveException` во внутренней цепочке `NModbusTransport.MapException`).
Пример обработки конкретного кода:

```csharp
try
{
    ushort[] data = await device.ReadHoldingRegistersAsync(0x2001, 2, ct);
}
catch (MvkModbusException ex) when (ex.ErrorKind == MvkErrorKind.DeviceError)
{
    Console.WriteLine($"Устройство ответило Modbus 0x{ex.ModbusExceptionCode:X2}");
}
catch (MvkModbusException ex) when (ex.IsTransient)
{
    // Связь: реконнект уже идёт в фоне.
    Console.WriteLine($"Нет связи: {ex.ErrorKind} — {ex.Message}");
}
```

### Логирование

`NModbusTransport` пишет в `ILogger<NModbusTransport>`:

- `Debug` — установка соединения (`МВК 'mvk-1': соединение установлено [host:port], SlaveId 1.`);
- `Warning` — ошибка обращения, вместе с исключением.

`MvkDevice` пишет `Information` о восстановлении соединения и `Debug`
о неудачном реконнекте и закрытии простаивающего соединения.
Подключите любой провайдер логирования — специальная настройка не требуется.

## 10. Особенность МВК01: чётные адрес и количество регистров

Устройство принимает только запросы с чётным адресом и чётным числом регистров;
иначе возвращает ответ-исключение с кодом Modbus **0x01**. Это подтверждено
на реальном МВК01 и учтено в настройках библиотеки.

Как это выглядит без локальной проверки (например, при прямом использовании
`NModbusTransport` или `RequireEvenAddressAndQuantity = false`):

```csharp
// Адрес 0x2001 нечётный, количество 1 нечётное.
ModbusResponse response = await device.ExecuteAsync(
    ModbusRequest.ReadHoldingRegisters("mvk-1", 0x2001, 1));

// response.IsSuccess            == false
// response.Error.ErrorKind      == MvkErrorKind.DeviceError
// response.Error.IsTransient    == false   (повторять бессмысленно)
// response.Error.ModbusExceptionCode == 0x01
// response.Error.Message        == "Устройство 'mvk-1' вернуло ошибку Modbus 0x01 (функция 0x83)."
```

Как включить проверку: параметр `RequireEvenAddressAndQuantity`. В JSON-конфигурации:

```json
{
  "MvkModbus": {
    "Defaults": {
      "RequireEvenAddressAndQuantity": true
    },
    "Devices": [
      { "Id": "mvk-1", "Host": "10.20.0.211" }
    ]
  }
}
```

Программно — через `MvkDefaultsOptions` (наследуется устройствами):

```csharp
MvkDefaultsOptions defaults = new()
{
    RequireEvenAddressAndQuantity = true
};

MvkDeviceOptions deviceOptions = new()
{
    Id = "mvk-1",
    Host = "10.20.0.211"
};

MvkDeviceSettings settings = deviceOptions.Resolve(defaults);
```

Что даёт проверка: запрос с нечётным адресом или количеством коротко замыкается
`ValidationMiddleware` **до** обращения к устройству — сеть не тратится,
а ошибка приходит как `MvkErrorKind.Validation`:

```csharp
ModbusResponse response = await device.ExecuteAsync(
    ModbusRequest.ReadHoldingRegisters("mvk-1", 0x2001, 1));

// response.Error.ErrorKind == MvkErrorKind.Validation
// response.Error.Message    == "Адрес 0x2001 и число регистров 1 должны быть чётными
//                              (особенность устройства 'mvk-1')."
```

Практические следствия для сырых запросов к МВК01:

- количество регистров чтения — чётное, максимум 124 (125 нечётно и не пройдёт);
- количество регистров записи — чётное;
- `WriteSingleRegisterAsync` (функция 0x06, `Quantity = 1`) при включённой проверке
  всегда отклоняется локально — используйте `WriteMultipleRegistersAsync`
  с чётным числом регистров;
- 86 регистров спектра и 16 регистров параметра по номеру — чётные, проходят без чанков.

## 11. Подводные камни

1. **`ExecuteAsync` не выбрасывает ошибок.** Если не проверять `IsSuccess`,
   ошибка связи/устройства останется незамеченной. Обёртки
   (`ReadHoldingRegistersAsync` и др.) выбрасывают `MvkModbusException` — выбирайте
   контракт осознанно.
2. **У обёрток нет перегрузки с таймаутом.** Общий таймаут операции задаётся только
   через `ExecuteAsync(request, timeout, ct)`; кроме того, middleware `timeout`
   должен присутствовать в пайплайне (по умолчанию он первый).
3. **Политика записи по умолчанию.** `WritePolicy = ProtectConfigArea` блокирует
   запись в 0x0000–0x0FFF (`Validation`), пока не задано `AllowConfigurationWrite = true`
   (в настройках устройства или `Defaults`). Для экспериментов с записью
   используйте `WritePolicy = AllowAll`.
4. **Чётность ломает одиночную запись.** При `RequireEvenAddressAndQuantity = true`
   `WriteSingleRegisterAsync` (количество 1) не проходит локальную проверку.
   На МВК01 пакетная запись чётным числом регистров — основной путь.
5. **Максимум 125 регистров за запрос.** Большие диапазоны режьте на чанки;
   на МВК01 с проверкой чётности практический максимум — 124 регистра.
6. **`Data` у записи — пустой массив, а не `null`.** Не пытайтесь читать данные
   из ответа на запись: `WriteSuccess().Data.Length == 0`.
7. **`WriteData` хранится по ссылке.** Массив, переданный в
   `ModbusRequest.WriteMultipleRegisters`, нельзя изменять после создания запроса.
8. **Прямой `NModbusTransport` обходит пайплайн.** Нет ни ретраев, ни проверки
   чётности, ни защиты записи, ни реконнекта: ошибки возвращаются «как есть»,
   а повтор — забота вызывающего кода.
9. **После ошибки соединение закрывается.** Следующий `ExecuteAsync` переподключится,
   поэтому к времени ответа добавится `ConnectTimeoutMs`. При работе через пул
   реконнектом занимается фоновое обслуживание (`Pool.ReconnectEnabled`).
10. **`LastError` живёт до первого успеха.** Невременные ошибки (`DeviceError`,
    `Validation`) не переводят устройство в `Faulted`, но записываются в `LastError`;
    ориентируйтесь на `IsTransient`, а не только на наличие последней ошибки.
11. **Отмена — это `OperationCanceledException`,** а не `MvkModbusException`.
    Обрабатывайте её отдельно, чтобы не принять штатное завершение за сбой.
12. **`RegisterCodec.ToUInt32`/`ToSingle` требуют минимум двух регистров.**
    Для срезов используйте `AsSpan(offset, 2)`; при ошибке — `ArgumentException`.
13. **`ToText` обрезает строку по нулевому байту и удаляет пробелы по краям.**
    Если нужны фиксированная длина или пробелы внутри, работайте через `ToBytes`.
14. **`GetBit(uint, ...)` — не то же, что `GetBit(ushort, ...)`.** Для 32-битных
    индикаторов сначала декодируйте `ToUInt32`, иначе биты 16–31 недоступны.
15. **Номер параметра — 1-базовый.** `0x4000 + (N−1)×16`; для `N = 48` это 0x42F0
    (в `MVK01_registers.txt` для этого параметра ошибочно указан 0x41F0).

## 12. См. также

- [Пул устройств](05-device-pool.md) — создание `IMvkDevice`, состояния, реконнект.
- [Карта регистров](07a-mvk01-register-map-and-codec.md) — адреса и формулы МВК01.
- [README](../README.md) — быстрый старт, конфигурация, ошибки и примеры.
