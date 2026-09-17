# Глава 12a. Справочник API: Mvk.Modbus

Полный справочник по публичному API проекта `src/Mvk.Modbus` (сборка `Mvk.Modbus`,
целевая платформа `net8.0`, включены `Nullable` и `ImplicitUsings`). Все сигнатуры
и значения по умолчанию сверены с исходниками `src/Mvk.Modbus/**/*.cs`.

Стиль изложения: для каждого типа указано полное пространство имён, назначение,
таблица публичных членов и, где это полезно, мини-пример. Публичные члены
приведены с точными типами параметров и возвращаемыми типами.

Содержание:

1. [Пространство имён Mvk.Modbus](#1-пространство-имён-mvkmodbus)
2. [Пространство имён Mvk.Modbus.Configuration](#2-пространство-имён-mvkmodbusconfiguration)
3. [Пространство имён Mvk.Modbus.Protocol](#3-пространство-имён-mvkmodbusprotocol)
4. [Пространство имён Mvk.Modbus.Pipeline](#4-пространство-имён-mvkmodbuspipeline)
5. [Пространство имён Mvk.Modbus.Pipeline.Middlewares](#5-пространство-имён-mvkmodbuspipelinemiddlewares)
6. [Пространство имён Mvk.Modbus.Pool](#6-пространство-имён-mvkmodbuspool)
7. [Пространство имён Mvk.Modbus.Transport](#7-пространство-имён-mvkmodbustransport)
8. [Пространство имён Mvk.Modbus.DependencyInjection](#8-пространство-имён-mvkmodbusdependencyinjection)
9. [Карта типов по пространствам имён](#9-карта-типов-по-пространствам-имён)
10. [См. также](#см-также)

---

## 1. Пространство имён Mvk.Modbus

В корневом пространстве имён находятся категория ошибок и типизированное исключение
библиотеки — общая модель ошибок для транспорта, пула, пайплайна и кодеков.

### 1.1. `Mvk.Modbus.MvkErrorKind`

Категория ошибки при работе с Modbus-устройством. Перечисление с базовым типом `int`.

| Сигнатура | Описание |
|---|---|
| `Unknown = 0` | Неизвестная ошибка. |
| `Transport = 1` | Ошибка транспорта: сеть, сокет, разрыв соединения. |
| `Timeout = 2` | Истёк таймаут подключения или ожидания ответа устройства. |
| `DeviceError = 3` | Устройство вернуло код ошибки Modbus (exception response). |
| `DeviceNotFound = 4` | Устройство не найдено в пуле. |
| `DeviceDisabled = 5` | Устройство отключено в конфигурации (`Enabled = false`). |
| `Validation = 6` | Некорректный запрос или нарушение политики записи. |
| `Canceled = 7` | Операция отменена вызывающим кодом. |
| `Busy = 8` | Устройство занято: очередь запросов переполнена или превышено время ожидания. |
| `Configuration = 9` | Ошибка конфигурации устройства или секции настроек. |

Примечания:

- Библиотека формирует значения `Transport`, `Timeout`, `DeviceError`,
  `DeviceNotFound`, `DeviceDisabled`, `Validation`, `Configuration`, а также
  `Unknown` (пайплайн завершился без ответа).
- Значения `Canceled` и `Busy` библиотека сама в текущей версии не создаёт — они
  предусмотрены для расширений (пользовательский транспорт или middleware).
  При этом `Busy` учитывается свойством `MvkModbusException.IsTransient`.

### 1.2. `Mvk.Modbus.MvkModbusException`

Исключение при работе с Modbus-устройством. Содержит категорию ошибки
`MvkErrorKind`, идентификатор устройства и код ошибки Modbus. Наследует
`System.Exception`, не является `sealed` (можно наследовать в прикладном коде).

Свойства:

| Сигнатура | Описание |
|---|---|
| `MvkErrorKind ErrorKind { get; }` | Категория ошибки. |
| `string? DeviceId { get; }` | Идентификатор устройства из конфигурации (может быть не задан). |
| `byte? ModbusExceptionCode { get; }` | Код ошибки Modbus из ответа (exception response). Заполняется только для `MvkErrorKind.DeviceError`. |
| `bool IsTransient { get; }` | Признак временной ошибки, которую имеет смысл повторять: `true` для `Transport`, `Timeout`, `Busy`. |

Конструкторы:

| Сигнатура | Описание |
|---|---|
| `MvkModbusException(string message)` | Создаёт исключение с категорией `MvkErrorKind.Unknown`. |
| `MvkModbusException(string message, MvkErrorKind errorKind)` | Создаёт исключение с указанной категорией ошибки. |
| `MvkModbusException(string message, MvkErrorKind errorKind, Exception? innerException)` | То же, с вложенным исключением. |
| `MvkModbusException(string message, MvkErrorKind errorKind, string? deviceId, byte? modbusExceptionCode = null, Exception? innerException = null)` | Полный набор данных об ошибке. |

Пример обработки:

```csharp
try
{
    ushort[] data = await device.ReadInputRegistersAsync(0x1004, 2);
}
catch (MvkModbusException ex) when (ex.ErrorKind == MvkErrorKind.DeviceError)
{
    Console.WriteLine($"Устройство {ex.DeviceId}: код Modbus 0x{ex.ModbusExceptionCode:X2}");
}
catch (MvkModbusException ex) when (ex.IsTransient)
{
    Console.WriteLine($"Временная ошибка ({ex.ErrorKind}): {ex.Message}");
}
```

---

## 2. Пространство имён Mvk.Modbus.Configuration

Модель конфигурации библиотеки: корневая секция `MvkModbus`, настройки по умолчанию,
описания устройств, порядок middleware, загрузчик и валидатор.

### 2.1. `Mvk.Modbus.Configuration.MvkByteOrder`

Порядок передачи байт для 32-битных значений (регистр 0x000C устройства МВК01).
Перечисление с базовым типом `int`.

| Сигнатура | Описание |
|---|---|
| `BigEndian = 0` | Big Endian, старшее слово первым (значение регистра 0x00000000, по умолчанию). |
| `LittleEndian = 1` | Little Endian, полный обратный порядок байт (значение регистра 0x66666666). |
| `Mixed = 2` | Смешанный режим, слова меняются местами (значение регистра 0x99999999, для ВДМ82). |

Порядок влияет на методы `RegisterCodec.ToUInt32` / `FromUInt32` / `ToSingle` / `FromSingle`.

### 2.2. `Mvk.Modbus.Configuration.MvkWritePolicy`

Политика записи в регистры устройства. Перечисление с базовым типом `int`.

| Сигнатура | Описание |
|---|---|
| `AllowAll = 0` | Запись разрешена во все регистры. |
| `ProtectConfigArea = 1` | Запись запрещена в защищённую область конфигурации; остальные регистры доступны. |
| `ReadOnly = 2` | Запись запрещена полностью (устройство только для чтения). |

Политику применяет `WriteProtectionMiddleware`; при нарушении запрос коротко
замыкается ошибкой `MvkErrorKind.Validation` без обращения к устройству.

### 2.3. `Mvk.Modbus.Configuration.MvkDefaultsOptions`

Настройки по умолчанию для всех устройств пула. Могут переопределяться на уровне
отдельного устройства (`MvkDeviceOptions`). Публичный конструктор — неявный
(параметров нет). Класс `sealed`.

| Сигнатура | Тип | Значение по умолчанию | Описание |
|---|---|---|---|
| `Port { get; set; }` | `int` | `502` | TCP-порт Modbus. |
| `SlaveId { get; set; }` | `byte` | `1` | Идентификатор устройства Modbus (slave id). |
| `ByteOrder { get; set; }` | `MvkByteOrder` | `MvkByteOrder.BigEndian` | Порядок передачи байт для 32-битных значений. |
| `ConnectTimeoutMs { get; set; }` | `int` | `3000` | Таймаут подключения, мс. |
| `ReadTimeoutMs { get; set; }` | `int` | `1000` | Таймаут чтения, мс. |
| `WriteTimeoutMs { get; set; }` | `int` | `3000` | Таймаут записи, мс. |
| `MaxRetries { get; set; }` | `int` | `2` | Число повторных попыток при временных ошибках. |
| `RetryDelayMs { get; set; }` | `int` | `150` | Базовая задержка между повторными попытками, мс. |
| `ReconnectBaseDelayMs { get; set; }` | `int` | `500` | Базовая задержка реконнекта, мс. |
| `ReconnectMaxDelayMs { get; set; }` | `int` | `30000` | Максимальная задержка реконнекта (ограничение экспоненциального роста), мс. |
| `IdleDisconnectSeconds { get; set; }` | `int` | `120` | Время простоя, после которого соединение закрывается, с. `0` — не закрывать. |
| `MaxConcurrentRequests { get; set; }` | `int` | `1` | Максимальное число одновременных запросов к одному устройству. |
| `WritePolicy { get; set; }` | `MvkWritePolicy` | `MvkWritePolicy.ProtectConfigArea` | Политика записи. |
| `AllowConfigurationWrite { get; set; }` | `bool` | `false` | Разрешить запись в защищённую область конфигурации при политике `ProtectConfigArea`. |
| `ProtectedWriteStartAddress { get; set; }` | `ushort` | `0x0000` | Начало защищённой области конфигурации (включительно). |
| `ProtectedWriteEndAddress { get; set; }` | `ushort` | `0x0FFF` | Конец защищённой области конфигурации (включительно). |
| `RequireEvenAddressAndQuantity { get; set; }` | `bool` | `false` | Требовать чётные адрес и число регистров (особенность МВК01: только 32-битные пары). |

На устройство переопределяются только `Port`, `SlaveId`, `ByteOrder`,
`MaxConcurrentRequests` и `AllowConfigurationWrite`; остальные параметры действуют
на весь пул (см. `MvkDeviceOptions.Resolve`).

### 2.4. `Mvk.Modbus.Configuration.MvkDeviceOptions`

Настройки одного устройства из конфигурации. Незаданные значения наследуются из
`MvkDefaultsOptions`. Публичный конструктор — неявный. Класс `sealed`.

| Сигнатура | Тип | Значение по умолчанию | Описание |
|---|---|---|---|
| `Id { get; set; }` | `string` | `string.Empty` | Уникальный идентификатор устройства в пуле (обязателен). |
| `Name { get; set; }` | `string?` | `null` | Отображаемое имя устройства. |
| `Host { get; set; }` | `string` | `string.Empty` | Адрес устройства (IP или DNS-имя, обязателен). |
| `Port { get; set; }` | `int?` | `null` | TCP-порт Modbus; `null` — взять из настроек по умолчанию. |
| `SlaveId { get; set; }` | `byte?` | `null` | Идентификатор устройства Modbus; `null` — из настроек по умолчанию. |
| `ByteOrder { get; set; }` | `MvkByteOrder?` | `null` | Порядок передачи байт; `null` — из настроек по умолчанию. |
| `Enabled { get; set; }` | `bool` | `true` | Устройство включено в работу. |
| `MaxConcurrentRequests { get; set; }` | `int?` | `null` | Максимальное число одновременных запросов; `null` — из настроек по умолчанию. |
| `AllowConfigurationWrite { get; set; }` | `bool?` | `null` | Разрешить запись в защищённую область; `null` — из настроек по умолчанию. |
| `Tags { get; set; }` | `Dictionary<string, string>?` | `null` | Произвольные метки устройства (расположение, агрегат и т.п.). |

Методы:

| Сигнатура | Описание |
|---|---|
| `MvkDeviceSettings Resolve(MvkDefaultsOptions defaults)` | Применяет настройки по умолчанию и возвращает неизменяемый набор параметров устройства. |

Поведение `Resolve`:

- `ArgumentNullException`, если `defaults` равен `null`.
- `Id` и `Host` обрезаются (`Trim()`); `Tags` копируются в словарь с компаратором
  `StringComparer.OrdinalIgnoreCase` (при `null` создаётся пустой словарь).
- Таймауты, ретраи, `WritePolicy`, защищённая область и
  `RequireEvenAddressAndQuantity` всегда берутся из `defaults`.

```csharp
MvkDeviceOptions device = new() { Id = "mvk-1", Host = "10.20.0.211", SlaveId = 2 };
MvkDeviceSettings settings = device.Resolve(new MvkDefaultsOptions());
Console.WriteLine(settings.Endpoint); // 10.20.0.211:502
```

### 2.5. `Mvk.Modbus.Configuration.MvkDeviceSettings`

Неизменяемые параметры устройства после применения настроек по умолчанию.
`sealed record` с `init`-свойствами — поддерживает сравнение по значению
и `with`-выражения. Все свойства обязательны (`required`) при создании через
инициализатор, кроме `Name`.

| Сигнатура | Тип | Описание |
|---|---|---|
| `Id { get; init; }` | `string` | Уникальный идентификатор устройства в пуле. Обязательное. |
| `Name { get; init; }` | `string?` | Отображаемое имя устройства. |
| `Host { get; init; }` | `string` | Адрес устройства (IP или DNS-имя). Обязательное. |
| `Port { get; init; }` | `int` | TCP-порт Modbus. Обязательное. |
| `SlaveId { get; init; }` | `byte` | Идентификатор устройства Modbus. Обязательное. |
| `ByteOrder { get; init; }` | `MvkByteOrder` | Порядок передачи байт для 32-битных значений. Обязательное. |
| `Enabled { get; init; }` | `bool` | Устройство включено в работу. Обязательное. |
| `MaxConcurrentRequests { get; init; }` | `int` | Максимальное число одновременных запросов к устройству. Обязательное. |
| `AllowConfigurationWrite { get; init; }` | `bool` | Запись в защищённую область конфигурации разрешена. Обязательное. |
| `ConnectTimeoutMs { get; init; }` | `int` | Таймаут подключения, мс. Обязательное. |
| `ReadTimeoutMs { get; init; }` | `int` | Таймаут чтения, мс. Обязательное. |
| `WriteTimeoutMs { get; init; }` | `int` | Таймаут записи, мс. Обязательное. |
| `MaxRetries { get; init; }` | `int` | Число повторных попыток при временных ошибках. Обязательное. |
| `RetryDelayMs { get; init; }` | `int` | Базовая задержка между повторными попытками, мс. Обязательное. |
| `ReconnectBaseDelayMs { get; init; }` | `int` | Базовая задержка реконнекта, мс. Обязательное. |
| `ReconnectMaxDelayMs { get; init; }` | `int` | Максимальная задержка реконнекта, мс. Обязательное. |
| `IdleDisconnectSeconds { get; init; }` | `int` | Время простоя, после которого соединение закрывается, с. Обязательное. |
| `WritePolicy { get; init; }` | `MvkWritePolicy` | Политика записи в регистры. Обязательное. |
| `ProtectedWriteStartAddress { get; init; }` | `ushort` | Начало защищённой области конфигурации (включительно). Обязательное. |
| `ProtectedWriteEndAddress { get; init; }` | `ushort` | Конец защищённой области конфигурации (включительно). Обязательное. |
| `RequireEvenAddressAndQuantity { get; init; }` | `bool` | Требовать чётные адрес и число регистров (особенность МВК01). Обязательное. |
| `Tags { get; init; }` | `IReadOnlyDictionary<string, string>` | Произвольные метки устройства. Обязательное. |
| `Endpoint { get; }` | `string` | Вычисляемое: адрес в формате `Host:Port`. |

Примечание: публичный конструктор неявный (без параметров), но при создании через
инициализатор обязательны все `required`-свойства. Типовой путь получения
экземпляра — `MvkDeviceOptions.Resolve`.

### 2.6. `Mvk.Modbus.Configuration.MvkPoolOptions`

Настройки пула устройств. Публичный конструктор — неявный. Класс `sealed`.

| Сигнатура | Тип | Значение по умолчанию | Описание |
|---|---|---|---|
| `ReconnectEnabled { get; set; }` | `bool` | `true` | Фоновое восстановление соединений и health-check. |
| `HealthCheckIntervalSeconds { get; set; }` | `int` | `15` | Период фоновой проверки устройств (реконнект, idle-отключение), с. При создании цикла значение приводится к минимуму `1`. |

### 2.7. `Mvk.Modbus.Configuration.MvkModbusOptions`

Корневая секция конфигурации библиотеки (`MvkModbus`). Класс `sealed`;
публичный конструктор — неявный.

| Сигнатура | Тип | Значение по умолчанию | Описание |
|---|---|---|---|
| `Defaults { get; set; }` | `MvkDefaultsOptions` | `new()` | Настройки по умолчанию для всех устройств. |
| `Pool { get; set; }` | `MvkPoolOptions` | `new()` | Настройки пула устройств. |
| `Pipeline { get; set; }` | `List<string>?` | `null` | Порядок middleware пайплайна (первый — самый внешний); `null` — порядок по умолчанию. |
| `Devices { get; set; }` | `List<MvkDeviceOptions>?` | `null` | Список устройств пула; `null` — пустой пул. |

Методы:

| Сигнатура | Описание |
|---|---|
| `IReadOnlyList<string> ResolvePipeline()` | Возвращает порядок middleware: из конфигурации либо порядок по умолчанию. |
| `IReadOnlyList<MvkDeviceSettings> ResolveDevices()` | Применяет настройки по умолчанию и возвращает параметры всех устройств пула. При `Devices == null` возвращает пустой массив. |
| `static List<string> CreateDefaultPipeline()` | Порядок по умолчанию: `timeout → logging → metrics → validation → retry → writeProtection`. |

```csharp
MvkModbusOptions options = new()
{
    Devices = [ new MvkDeviceOptions { Id = "mvk-1", Host = "10.20.0.211" } ]
};

IReadOnlyList<string> pipeline = options.ResolvePipeline();   // 6 имён по умолчанию
IReadOnlyList<MvkDeviceSettings> devices = options.ResolveDevices();
```

### 2.8. `Mvk.Modbus.Configuration.MvkModbusOptionsValidator`

Проверяет корректность секции `MvkModbus`: диапазоны значений, обязательные поля
и уникальность идентификаторов устройств. `sealed class`, реализует
`Microsoft.Extensions.Options.IValidateOptions<MvkModbusOptions>`. Публичный
конструктор — неявный.

| Сигнатура | Описание |
|---|---|
| `ValidateOptionsResult Validate(string? name, MvkModbusOptions options)` | Проверяет настройки. `name` не используется. При `options == null` возвращает `ValidateOptionsResult.Fail`. |

Что проверяется:

| Проверка | Диапазон / условие |
|---|---|
| `Defaults.Port` | `1..65535` |
| `Defaults.SlaveId` | `1..247` |
| `Defaults.ConnectTimeoutMs` | `1..600000` |
| `Defaults.ReadTimeoutMs` | `1..600000` |
| `Defaults.WriteTimeoutMs` | `1..600000` |
| `Defaults.MaxRetries` | `0..10` |
| `Defaults.RetryDelayMs` | `0..60000` |
| `Defaults.ReconnectBaseDelayMs` | `1..600000` |
| `Defaults.ReconnectMaxDelayMs` | `1..3600000` |
| `Defaults.IdleDisconnectSeconds` | `0..86400` |
| `Defaults.MaxConcurrentRequests` | `1..64` |
| `Defaults.ReconnectMaxDelayMs < Defaults.ReconnectBaseDelayMs` | ошибка |
| `Defaults.ProtectedWriteEndAddress < Defaults.ProtectedWriteStartAddress` | ошибка |
| Устройство: `Id` | обязателен, уникален (без учёта регистра) |
| Устройство: `Host` | обязателен (не пустой и не пробелы) |
| Устройство: `Port`, `SlaveId`, `MaxConcurrentRequests`, `ConnectTimeoutMs`, `ReadTimeoutMs`, `WriteTimeoutMs`, `MaxRetries`, `IdleDisconnectSeconds` | те же диапазоны, что и для `Defaults` (берутся из `Resolve`) |
| `Devices` | пустой элемент списка — ошибка |

При отсутствии ошибок возвращается `ValidateOptionsResult.Success`, иначе —
`ValidateOptionsResult.Fail` со списком всех найденных нарушений.

### 2.9. `Mvk.Modbus.Configuration.MvkModbusOptionsLoader`

Загрузка конфигурации библиотеки из JSON-файла без использования DI-контейнера.
`static class`.

| Сигнатура | Описание |
|---|---|
| `const string DefaultSectionName = "MvkModbus"` | Имя секции конфигурации по умолчанию. |
| `static IConfigurationRoot BuildConfiguration(string jsonFilePath)` | Создаёт конфигурацию из JSON-файла. Бросает `ArgumentException` при пустом пути и `MvkModbusException` (`Configuration`) если файл не найден. |
| `static MvkModbusOptions LoadFromJsonFile(string jsonFilePath, string sectionName = DefaultSectionName)` | Загружает и проверяет секцию из JSON-файла. |
| `static MvkModbusOptions LoadFromConfiguration(IConfiguration configuration, string sectionName = DefaultSectionName)` | Загружает и проверяет секцию из готовой конфигурации. |

Исключения `LoadFromJsonFile` / `LoadFromConfiguration` (обе с
`MvkErrorKind.Configuration`):

- секция не найдена (`null`);
- конфигурация не прошла валидацию `MvkModbusOptionsValidator`.

Также: `LoadFromConfiguration` бросает `ArgumentNullException` при
`configuration == null` и `ArgumentException` при пустом `sectionName`.

```csharp
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Transport;

MvkModbusOptions options = MvkModbusOptionsLoader.LoadFromJsonFile("mvkmodbus.json");
MvkDeviceSettings settings = options.ResolveDevices()[0];
IMvkTransport transport = new NModbusTransportFactory().Create(settings);
```

---

## 3. Пространство имён Mvk.Modbus.Protocol

Протокольные примитивы: операция, неизменяемые запрос и ответ, кодеки регистров.

### 3.1. `Mvk.Modbus.Protocol.ModbusOperation`

Операция Modbus, выполняемая над устройством. Перечисление с базовым типом `int`;
значения совпадают с кодами функций Modbus.

| Сигнатура | Описание |
|---|---|
| `ReadHoldingRegisters = 0x03` | Чтение регистров хранения (функция 0x03). |
| `ReadInputRegisters = 0x04` | Чтение входных регистров (функция 0x04). |
| `WriteSingleRegister = 0x06` | Запись одного регистра хранения (функция 0x06). |
| `WriteMultipleRegisters = 0x10` | Запись нескольких регистров хранения (функция 0x10). |

### 3.2. `Mvk.Modbus.Protocol.ModbusRequest`

Неизменяемый запрос к Modbus-устройству. Класс `sealed`; публичного конструктора
нет — экземпляры создаются только статическими фабриками.

Константы:

| Сигнатура | Значение | Описание |
|---|---|---|
| `const int MaxReadRegisters` | `125` | Максимальное число регистров в одном запросе чтения (ограничение Modbus). |
| `const int MaxWriteRegisters` | `123` | Максимальное число регистров в одном запросе записи (ограничение Modbus). |

Свойства:

| Сигнатура | Описание |
|---|---|
| `string DeviceId { get; }` | Идентификатор устройства из конфигурации. |
| `ModbusOperation Operation { get; }` | Выполняемая операция. |
| `ushort StartAddress { get; }` | Адрес первого регистра. |
| `ushort Quantity { get; }` | Число регистров в запросе. |
| `ushort[]? WriteData { get; }` | Данные для записи; `null` для операций чтения. Для `WriteSingleRegister` — массив из одного значения. |
| `bool IsWrite { get; }` | `true` для `WriteSingleRegister` и `WriteMultipleRegisters`. |
| `int EndAddress { get; }` | Последний затрагиваемый регистр (включительно): `StartAddress + Quantity - 1`. |

Фабрики:

| Сигнатура | Описание |
|---|---|
| `static ModbusRequest ReadHoldingRegisters(string deviceId, ushort startAddress, ushort quantity)` | Создаёт запрос чтения регистров хранения (0x03). |
| `static ModbusRequest ReadInputRegisters(string deviceId, ushort startAddress, ushort quantity)` | Создаёт запрос чтения входных регистров (0x04). |
| `static ModbusRequest WriteSingleRegister(string deviceId, ushort address, ushort value)` | Создаёт запрос записи одного регистра (0x06), `Quantity = 1`. |
| `static ModbusRequest WriteMultipleRegisters(string deviceId, ushort startAddress, ushort[] data)` | Создаёт запрос записи нескольких регистров (0x10), `Quantity = data.Length`. |

Правила проверки (исключения при создании):

- `deviceId` пуст или пробелы — `ArgumentException`;
- для операции чтения переданы данные записи — `ArgumentException`;
- для операции записи данные не переданы — `ArgumentException`;
- `quantity == 0` или превышает лимит (125 для чтения, 123 для записи) —
  `ArgumentOutOfRangeException`;
- длина `WriteData` не совпадает с `Quantity` — `ArgumentException`;
- `StartAddress + Quantity` выходит за границы адресного пространства Modbus
  (`ushort.MaxValue + 1`) — `ArgumentOutOfRangeException`;
- `WriteMultipleRegisters` с пустым или слишком длинным массивом —
  `ArgumentException` (до входа в общий конструктор).

```csharp
ModbusRequest read = ModbusRequest.ReadHoldingRegisters("mvk-1", 0x1004, 2);
ModbusRequest write = ModbusRequest.WriteMultipleRegisters("mvk-1", 0x0100, [0x0001, 0x0002]);

Console.WriteLine(read.EndAddress); // 4101
```

### 3.3. `Mvk.Modbus.Protocol.ModbusResponse`

Результат выполнения `ModbusRequest`: данные ответа либо ошибка. Класс `sealed`;
публичного конструктора нет — экземпляры создаются статическими фабриками.

Свойства:

| Сигнатура | Описание |
|---|---|
| `bool IsSuccess { get; }` | Признак успешного выполнения запроса. |
| `ushort[] Data { get; }` | Данные ответа (для операций чтения). Для операций записи и ошибок — пустой массив. |
| `MvkModbusException? Error { get; }` | Ошибка выполнения запроса; `null` при успехе. |

Фабрики:

| Сигнатура | Описание |
|---|---|
| `static ModbusResponse Success(ushort[] data)` | Успешный ответ с данными. `data == null` — `ArgumentNullException`. |
| `static ModbusResponse WriteSuccess()` | Успешный ответ для операции записи (пустой массив данных). |
| `static ModbusResponse Failure(MvkModbusException error)` | Ответ с ошибкой. `error == null` — `ArgumentNullException`. |
| `static ModbusResponse Failure(Exception exception, string? deviceId = null)` | Ответ с ошибкой из произвольного исключения. Если `exception` уже `MvkModbusException` — используется как есть; иначе оборачивается в `MvkModbusException` с `MvkErrorKind.Transport` и вложенным исключением. |

```csharp
ModbusResponse response = await device.ExecuteAsync(
    ModbusRequest.ReadInputRegisters("mvk-1", 0x1004, 2));

if (response.IsSuccess)
{
    Console.WriteLine($"Регистры: {string.Join(", ", response.Data)}");
}
else
{
    Console.WriteLine($"Ошибка {response.Error!.ErrorKind}: {response.Error.Message}");
}
```

### 3.4. `Mvk.Modbus.Protocol.RegisterCodec`

Преобразование 16-битных регистров Modbus в типизированные значения и обратно
с учётом порядка передачи байт устройства (`MvkByteOrder`, регистр 0x000C МВК01).
`static class` — только статические методы, состояние не хранит.

Методы:

| Сигнатура | Описание |
|---|---|
| `static uint ToUInt32(ReadOnlySpan<ushort> registers, MvkByteOrder byteOrder)` | Декодирует 32-битное беззнаковое значение из двух регистров. |
| `static int ToInt32(ReadOnlySpan<ushort> registers, MvkByteOrder byteOrder)` | Декодирует 32-битное знаковое значение из двух регистров. |
| `static float ToSingle(ReadOnlySpan<ushort> registers, MvkByteOrder byteOrder)` | Декодирует `float` (single) из двух регистров. |
| `static ushort[] FromUInt32(uint value, MvkByteOrder byteOrder)` | Кодирует 32-битное беззнаковое значение в два регистра. |
| `static ushort[] FromSingle(float value, MvkByteOrder byteOrder)` | Кодирует `float` (single) в два регистра. |
| `static byte[] ToBytes(ReadOnlySpan<ushort> registers, int byteCount = -1)` | Преобразует регистры в байты: старший байт регистра — первый. |
| `static string ToText(ReadOnlySpan<ushort> registers, Encoding encoding, int byteCount = -1)` | Декодирует строку из регистров. |
| `static ushort[] FromText(string text, int registerCount, Encoding encoding)` | Кодирует строку в регистры. |
| `static bool GetBit(ushort value, int bitIndex)` | Проверяет бит 16-битного значения (номер `0..15`). |
| `static bool GetBit(uint value, int bitIndex)` | Проверяет бит 32-битного значения (номер `0..31`). |

Подробности по каждому методу:

- `ToUInt32`: требует минимум два регистра (`ArgumentException` иначе). Для
  `BigEndian` — `registers[0] << 16 | registers[1]`; для `LittleEndian` — полный
  реверс байт; для `Mixed` — `registers[1] << 16 | registers[0]`. Неизвестное
  значение `byteOrder` — `ArgumentOutOfRangeException`.
- `ToInt32` — переинтерпретация `ToUInt32` (`unchecked((int)...)`).
- `ToSingle` — `BitConverter.UInt32BitsToSingle(ToUInt32(...))`.
- `FromUInt32`: для `BigEndian` и `Mixed` слова раскладываются по-разному:
  `Mixed` возвращает `[low, high]`, остальные — `[high, low]`.
- `ToBytes`: `byteCount < 0` — все байты регистров; иначе первые `byteCount` байт
  (обрезается по длине массива). Порядок внутри регистра — big-endian.
- `ToText`: строка обрезается по первому нулевому байту, результат дополнительно
  обрезается по пробелам (`Trim()`). `encoding == null` — `ArgumentNullException`.
- `FromText`: `registerCount` должен быть больше нуля (`ArgumentOutOfRangeException`);
  строка не должна занимать больше `registerCount * 2` байт (`ArgumentException`);
  свободные байты заполняются нулями; `text` или `encoding` равны `null` —
  `ArgumentNullException`.
- `GetBit`: `bitIndex` вне диапазона — `ArgumentOutOfRangeException`.

Примеры:

```csharp
using System.Text;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Protocol;

// 32-битные значения
ushort[] registers = [0x0001, 0x0002];
uint big = RegisterCodec.ToUInt32(registers, MvkByteOrder.BigEndian);       // 0x00010002
uint little = RegisterCodec.ToUInt32(registers, MvkByteOrder.LittleEndian); // 0x02000100
uint mixed = RegisterCodec.ToUInt32(registers, MvkByteOrder.Mixed);         // 0x00020001

ushort[] encoded = RegisterCodec.FromUInt32(0x00010002, MvkByteOrder.BigEndian); // { 0x0001, 0x0002 }
float value = RegisterCodec.ToSingle([0x3F80, 0x0000], MvkByteOrder.BigEndian);  // 1.0f

// Байты и строки
byte[] bytes = RegisterCodec.ToBytes([0x4142], 1);                               // { 0x41 }
string text = RegisterCodec.ToText([0x4142, 0x4300], Encoding.ASCII);            // "ABC"
ushort[] textRegisters = RegisterCodec.FromText("AB", 1, Encoding.ASCII);        // { 0x4142 }

// Биты
bool set = RegisterCodec.GetBit((ushort)0x0004, 2);                              // true
bool set32 = RegisterCodec.GetBit(0x80000000u, 31);                              // true
```

---

## 4. Пространство имён Mvk.Modbus.Pipeline

Абстракции пайплайна обработки запросов: делегат, интерфейс middleware, контекст,
ключи элементов, имена встроенных middleware, сборщик, собранный пайплайн и фабрика
наборов по умолчанию.

### 4.1. `Mvk.Modbus.Pipeline.InvokeModbusDelegate`

Обработчик запроса в пайплайне. Каждый middleware получает следующий обработчик
и может выполнить действие до/после него, подменить токен отмены, изменить ответ
или вовсе не вызывать следующий обработчик (short-circuit).

| Сигнатура | Описание |
|---|---|
| `delegate Task InvokeModbusDelegate(ModbusRequestContext context, CancellationToken cancellationToken)` | Асинхронный обработчик: контекст запроса и токен отмены для вложенных обработчиков. |

### 4.2. `Mvk.Modbus.Pipeline.IModbusMiddleware`

Middleware обработки запросов к Modbus-устройствам (аналог middleware в ASP.NET).

| Сигнатура | Описание |
|---|---|
| `Task InvokeAsync(ModbusRequestContext context, InvokeModbusDelegate next, CancellationToken cancellationToken)` | Обрабатывает запрос; для передачи управления дальше вызывает `next`. Токен может быть заменён на связанный (например, таймаутом). |

### 4.3. `Mvk.Modbus.Pipeline.ModbusRequestContext`

Контекст обработки одного запроса к устройству (аналог `HttpContext` в ASP.NET).
Класс `sealed`.

Конструктор:

| Сигнатура | Описание |
|---|---|
| `ModbusRequestContext(MvkDeviceSettings settings, ModbusRequest request, CancellationToken aborted)` | Создаёт контекст запроса. `settings == null` или `request == null` — `ArgumentNullException`. |

Свойства:

| Сигнатура | Описание |
|---|---|
| `MvkDeviceSettings Settings { get; }` | Параметры устройства. |
| `ModbusRequest Request { get; }` | Запрос к устройству. |
| `CancellationToken Aborted { get; }` | Токен отмены вызывающего кода. |
| `ModbusResponse? Response { get; set; }` | Ответ пайплайна. Заполняется терминальным обработчиком или middleware при коротком замыкании. |
| `int Attempt { get; set; }` | Номер текущей попытки (устанавливает `RetryMiddleware`). Начальное значение — `1`. |
| `IDictionary<string, object?> Items { get; }` | Произвольные данные, которыми middleware обмениваются в рамках запроса. Словарь с компаратором `StringComparer.Ordinal`, создаётся вместе с контекстом. |
| `bool IsCompleted { get; }` | `true`, если `Response` не `null` (ответ получен — успешный или с ошибкой). |

Методы:

| Сигнатура | Описание |
|---|---|
| `bool TryGetItem<T>(string key, out T value)` | Пробует получить значение из `Items`; возвращает `false`, если ключа нет или тип не совпадает. |

```csharp
ModbusRequestContext context = new(settings, request, CancellationToken.None);
context.Items[MvkContextItems.TimeoutMs] = 5000;
bool has = context.TryGetItem<int>(MvkContextItems.TimeoutMs, out int timeoutMs);
```

### 4.4. `Mvk.Modbus.Pipeline.MvkContextItems`

Ключи для `ModbusRequestContext.Items`. `static class`.

| Сигнатура | Значение | Описание |
|---|---|---|
| `const string TimeoutMs` | `"mvk.timeoutMs"` | Общий таймаут операции в миллисекундах (`int`). Устанавливает вызывающий код, например типизированный слой для команды сохранения конфигурации (3000 мс). |
| `const string ShortCircuited` | `"mvk.shortCircuited"` | Признак короткого замыкания пайплайна без обращения к устройству (`bool`). |

### 4.5. `Mvk.Modbus.Pipeline.MvkMiddlewareNames`

Имена встроенных middleware для секции `Pipeline` конфигурации. `static class`.
Сопоставление имён выполняется без учёта регистра.

| Сигнатура | Значение | Описание |
|---|---|---|
| `const string Timeout` | `"timeout"` | Ограничение общего времени операции (`TimeoutMiddleware`). |
| `const string Logging` | `"logging"` | Логирование запросов (`LoggingMiddleware`). |
| `const string Validation` | `"validation"` | Проверка запроса (`ValidationMiddleware`). |
| `const string Retry` | `"retry"` | Повторные попытки при временных ошибках (`RetryMiddleware`). |
| `const string Metrics` | `"metrics"` | Метрики (`MetricsMiddleware`). |
| `const string WriteProtection` | `"writeProtection"` | Защита от записи (`WriteProtectionMiddleware`). |

### 4.6. `Mvk.Modbus.Pipeline.MvkPipelineBuilder`

Сборщик пайплайна middleware. Класс `sealed`; публичный конструктор — неявный.

| Сигнатура | Описание |
|---|---|
| `IReadOnlyList<IModbusMiddleware> Middlewares { get; }` | Зарегистрированные middleware в порядке выполнения (первый — самый внешний). |
| `MvkPipelineBuilder Use(IModbusMiddleware middleware)` | Добавляет middleware в конец пайплайна и возвращает `this`. `middleware == null` — `ArgumentNullException`. |
| `MvkPipelineBuilder Use(Func<ModbusRequestContext, InvokeModbusDelegate, CancellationToken, Task> middleware)` | Добавляет middleware в виде функции и возвращает `this`. Функция оборачивается во внутренний `IModbusMiddleware`. |
| `MvkPipeline Build(InvokeModbusDelegate terminal)` | Собирает пайплайн с указанным терминальным обработчиком (обычно транспорт). `terminal == null` — `ArgumentNullException`. |

```csharp
IMvkTransport transport = new NModbusTransport(settings);
InvokeModbusDelegate terminal = MvkPipeline.TerminalFromTransport(transport);

MvkPipeline pipeline = new MvkPipelineBuilder()
    .Use(async (context, next, cancellationToken) =>
    {
        context.Items[MvkContextItems.TimeoutMs] = 3000;
        await next(context, cancellationToken);
    })
    .Use(new RetryMiddleware())
    .Build(terminal);
```

### 4.7. `Mvk.Modbus.Pipeline.MvkPipeline`

Собранный пайплайн обработки запросов: middleware + терминальный обработчик.
Класс `sealed`.

| Сигнатура | Описание |
|---|---|
| `MvkPipeline(InvokeModbusDelegate terminal, IEnumerable<IModbusMiddleware> middlewares)` | Создаёт пайплайн. Middleware перечисляются в порядке выполнения (первый — самый внешний). Любой из аргументов `null` — `ArgumentNullException`. |
| `static InvokeModbusDelegate TerminalFromTransport(IMvkTransport transport)` | Возвращает терминальный обработчик, вызывающий `transport.ExecuteAsync` и записывающий результат в `context.Response`. `transport == null` — `ArgumentNullException`. |
| `Task<ModbusResponse> ExecuteAsync(ModbusRequestContext context)` | Выполняет запрос через пайплайн и возвращает ответ. |

Поведение `ExecuteAsync`:

- Во время выполнения использует `context.Aborted` как токен отмены.
- Не бросает `MvkModbusException` для ошибок транспорта или устройства — такие
  ошибки приходят как `ModbusResponse` с заполненным `Error` (их возвращают
  middleware или терминальный обработчик).
- Бросает `MvkModbusException` (`MvkErrorKind.Unknown`, `DeviceId` из
  `context.Settings.Id`) только если пайплайн завершился без ответа
  (`context.Response == null`), то есть терминальный обработчик не был вызван.
- `context == null` — `ArgumentNullException`.

### 4.8. `Mvk.Modbus.Pipeline.MvkPipelineFactory`

Создаёт наборы middleware по умолчанию. `static class`.

| Сигнатура | Описание |
|---|---|
| `static IReadOnlyList<IModbusMiddleware> CreateDefaultMiddlewares(ILoggerFactory? loggerFactory = null)` | Создаёт middleware в порядке по умолчанию: `timeout → logging → metrics → validation → retry → writeProtection`. Если `loggerFactory` не задан, используется `NullLoggerFactory.Instance`. |

Состав возвращаемого набора (новые экземпляры при каждом вызове):

| Позиция | Экземпляр |
|---|---|
| 1 | `new TimeoutMiddleware()` |
| 2 | `new LoggingMiddleware(factory.CreateLogger<LoggingMiddleware>())` |
| 3 | `new MetricsMiddleware()` |
| 4 | `new ValidationMiddleware()` |
| 5 | `new RetryMiddleware()` |
| 6 | `new WriteProtectionMiddleware()` |

---

## 5. Пространство имён Mvk.Modbus.Pipeline.Middlewares

Встроенные middleware пайплайна. Все типы `sealed`, реализуют `IModbusMiddleware`;
кроме `LoggingMiddleware`, публичных конструкторов не объявляют (доступен неявный
конструктор без параметров).

### 5.1. `Mvk.Modbus.Pipeline.Middlewares.TimeoutMiddleware`

Ограничивает общее время операции. Таймаут задаётся в
`ModbusRequestContext.Items` по ключу `MvkContextItems.TimeoutMs`; если он не задан,
middleware не вмешивается (действуют таймауты сокета транспорта).

| Сигнатура | Описание |
|---|---|
| `Task InvokeAsync(ModbusRequestContext context, InvokeModbusDelegate next, CancellationToken cancellationToken)` | Читает `Items[TimeoutMs]` (`int`). Если значение отсутствует или `<= 0` — просто вызывает `next`. Иначе создаёт связанный `CancellationTokenSource` c `CancelAfter(timeoutMs)` и вызывает `next` с этим токеном. |

Примечание: если вложенный обработчик завершился по таймауту
(`OperationCanceledException`), а токен вызывающего кода не был отменён,
middleware записывает в `context.Response` ошибку `MvkErrorKind.Timeout`
с текстом `Таймаут операции ... — превышено N мс.` Если отменён именно токен
вызывающего кода, исключение не перехватывается.

### 5.2. `Mvk.Modbus.Pipeline.Middlewares.LoggingMiddleware`

Логирует каждый запрос: устройство, операцию, адрес, результат и длительность.
Успешные запросы — уровень `Debug`, ошибки — `Warning`.

| Сигнатура | Описание |
|---|---|
| `LoggingMiddleware(ILogger<LoggingMiddleware>? logger = null)` | Создаёт middleware логирования; при `null` используется `NullLogger<LoggingMiddleware>.Instance`. |
| `Task InvokeAsync(ModbusRequestContext context, InvokeModbusDelegate next, CancellationToken cancellationToken)` | Вызывает `next`, замеряет длительность через `Stopwatch.GetTimestamp()` / `Stopwatch.GetElapsedTime` и пишет одну запись в лог. |

В сообщение входят: `DeviceId`, `Operation`, адрес (`0x{Address:X4}`), количество
регистров, длительность в мс, номер попытки (`context.Attempt`) и, для ошибок,
категория (`ErrorKind`) и текст ошибки.

### 5.3. `Mvk.Modbus.Pipeline.Middlewares.MetricsMiddleware`

Публикует метрики запросов через `System.Diagnostics.Metrics`. Публичный
конструктор — неявный.

| Сигнатура | Описание |
|---|---|
| `Task InvokeAsync(ModbusRequestContext context, InvokeModbusDelegate next, CancellationToken cancellationToken)` | Вызывает `next`, затем записывает в счётчик `1`, а в гистограмму — длительность запроса. |

Метрики (создаются статически, `Meter` с именем `Mvk.Modbus`, версия `1.0.0`):

| Инструмент | Имя | Тип | Единица | Теги |
|---|---|---|---|---|
| Запросы | `mvk.modbus.requests` | `Counter<long>` | — | `device`, `operation`, `result` |
| Длительность | `mvk.modbus.duration` | `Histogram<double>` | `ms` | `device`, `operation`, `result` |

Тег `result` принимает значения `success` / `error`; `device` — `Settings.Id`;
`operation` — имя `ModbusOperation`.

### 5.4. `Mvk.Modbus.Pipeline.Middlewares.ValidationMiddleware`

Проверяет запрос перед обращением к устройству: ограничения Modbus и особенности
устройства (например, требование чётных адреса и числа регистров у МВК01).
При нарушении коротко замыкает пайплайн ошибкой `MvkErrorKind.Validation`.

| Сигнатура | Описание |
|---|---|
| `Task InvokeAsync(ModbusRequestContext context, InvokeModbusDelegate next, CancellationToken cancellationToken)` | Вызывает `Validate`; при ошибке записывает `ModbusResponse.Failure(error)` в контекст и не вызывает `next`. |
| `static MvkModbusException? Validate(ModbusRequestContext context)` | Проверяет запрос и возвращает ошибку либо `null`. `context == null` — `ArgumentNullException`. |

Что проверяет `Validate`:

- для операции записи отсутствуют данные (`request.WriteData is null`) — ошибка
  `MvkErrorKind.Validation`;
- если `settings.RequireEvenAddressAndQuantity`, то и `StartAddress`, и `Quantity`
  должны быть чётными, иначе ошибка с пояснением особенности устройства.

### 5.5. `Mvk.Modbus.Pipeline.Middlewares.RetryMiddleware`

Повторяет запрос при временных ошибках (`MvkModbusException.IsTransient`): число
попыток и задержка берутся из параметров устройства. Задержка растёт линейно.
Публичный конструктор — неявный.

| Сигнатура | Описание |
|---|---|
| `Task InvokeAsync(ModbusRequestContext context, InvokeModbusDelegate next, CancellationToken cancellationToken)` | Выполняет запрос до `MaxRetries + 1` попыток (минимум одна). Перед каждой попыткой устанавливает `context.Attempt` и сбрасывает `context.Response = null`. |

Алгоритм:

1. `maxAttempts = Math.Max(1, settings.MaxRetries + 1)`.
2. После вызова `next` попытка прекращается, если ответ получен и успешен, если
   ошибка не является временной (`IsTransient == false`) или попытки исчерпаны.
3. Задержка перед следующей попыткой — `settings.RetryDelayMs * attempt`
   (линейный рост); при нулевой задержке `Task.Delay` не вызывается.
4. `Task.Delay` использует `cancellationToken` — отмена вызывающего кода
   прерывает паузу.

### 5.6. `Mvk.Modbus.Pipeline.Middlewares.WriteProtectionMiddleware`

Запрещает запись по политике устройства: полный запрет (`MvkWritePolicy.ReadOnly`)
или защита области конфигурации (`MvkWritePolicy.ProtectConfigArea`). Чтение не
ограничивается. Публичный конструктор — неявный.

| Сигнатура | Описание |
|---|---|
| `Task InvokeAsync(ModbusRequestContext context, InvokeModbusDelegate next, CancellationToken cancellationToken)` | Вызывает `Check`; при отказе записывает `ModbusResponse.Failure(denial)` и не вызывает `next`. |
| `static MvkModbusException? Check(ModbusRequestContext context)` | Проверяет политику записи и возвращает ошибку либо `null`. `context == null` — `ArgumentNullException`. |

Правила `Check`:

1. Запрос на чтение — `null` (разрешено).
2. `WritePolicy == ReadOnly` — ошибка `MvkErrorKind.Validation`.
3. `WritePolicy == ProtectConfigArea` и `AllowConfigurationWrite == false`,
   и диапазон запроса (`[StartAddress, EndAddress]`) пересекается с защищённой
   областью (`[ProtectedWriteStartAddress, ProtectedWriteEndAddress]`) — ошибка
   `MvkErrorKind.Validation`.
4. Иначе — `null`.

Пересечение: `request.StartAddress <= settings.ProtectedWriteEndAddress` и
`request.EndAddress >= settings.ProtectedWriteStartAddress` (границы включительно).

---

## 6. Пространство имён Mvk.Modbus.Pool

Пул устройств: состояния, событие смены состояния, интерфейс устройства,
реализация устройства и пула.

### 6.1. `Mvk.Modbus.Pool.MvkDeviceState`

Состояние устройства в пуле. Перечисление с базовым типом `int`.

| Сигнатура | Описание |
|---|---|
| `Disconnected = 0` | Соединение не установлено. |
| `Connecting = 1` | Выполняется подключение. |
| `Connected = 2` | Соединение установлено, устройство отвечает. |
| `Faulted = 3` | Соединение потеряно или устройство не отвечает; ожидается реконнект. |

### 6.2. `Mvk.Modbus.Pool.MvkDeviceStateChangedEventArgs`

Данные события изменения состояния устройства. Наследует `System.EventArgs`,
класс `sealed`.

| Сигнатура | Описание |
|---|---|
| `MvkDeviceStateChangedEventArgs(IMvkDevice device, MvkDeviceState oldState, MvkDeviceState newState, MvkModbusException? error = null)` | Создаёт данные события. `device == null` — `ArgumentNullException`. |
| `IMvkDevice Device { get; }` | Устройство, у которого изменилось состояние. |
| `MvkDeviceState OldState { get; }` | Предыдущее состояние. |
| `MvkDeviceState NewState { get; }` | Новое состояние. |
| `MvkModbusException? Error { get; }` | Ошибка, вызвавшая переход (может отсутствовать). |

### 6.3. `Mvk.Modbus.Pool.IMvkDevice`

Устройство пула: сериализует запросы и выполняет их через пайплайн middleware.
Наследует `IDisposable`.

Важно: `ExecuteAsync` возвращает ответ, в том числе с ошибкой, и не выбрасывает
`MvkModbusException` при ошибках выполнения; удобные обёртки чтения/записи
(`ReadHoldingRegistersAsync`, `ReadInputRegistersAsync`, `WriteSingleRegisterAsync`,
`WriteMultipleRegistersAsync`) выбрасывают `MvkModbusException`.

Свойства и событие:

| Сигнатура | Описание |
|---|---|
| `MvkDeviceSettings Settings { get; }` | Параметры устройства (с применёнными настройками по умолчанию). |
| `MvkDeviceState State { get; }` | Текущее состояние. |
| `bool IsConnected { get; }` | Соединение установлено (делегируется транспорту). |
| `MvkModbusException? LastError { get; }` | Последняя ошибка обращения (сбрасывается при успехе). |
| `DateTimeOffset LastRequestAt { get; }` | Время последнего запроса к устройству (UTC). До первого запроса — `DateTimeOffset.MinValue`. |
| `event EventHandler<MvkDeviceStateChangedEventArgs>? StateChanged` | Событие изменения состояния устройства. |

Методы:

| Сигнатура | Описание |
|---|---|
| `Task<ModbusResponse> ExecuteAsync(ModbusRequest request, CancellationToken cancellationToken = default)` | Выполняет запрос через пайплайн; при необходимости подключается автоматически. |
| `Task<ModbusResponse> ExecuteAsync(ModbusRequest request, TimeSpan timeout, CancellationToken cancellationToken = default)` | То же с общим таймаутом операции (учитывается middleware таймаута, если он в пайплайне). |
| `Task EnsureConnectedAsync(CancellationToken cancellationToken = default)` | Устанавливает соединение явно (например, для проверки доступности). |
| `Task DisconnectAsync(CancellationToken cancellationToken = default)` | Закрывает соединение. |
| `Task<ushort[]> ReadHoldingRegistersAsync(ushort address, ushort quantity, CancellationToken cancellationToken = default)` | Читает регистры хранения (функция 0x03). |
| `Task<ushort[]> ReadInputRegistersAsync(ushort address, ushort quantity, CancellationToken cancellationToken = default)` | Читает входные регистры (функция 0x04). |
| `Task WriteSingleRegisterAsync(ushort address, ushort value, CancellationToken cancellationToken = default)` | Записывает один регистр (функция 0x06). |
| `Task WriteMultipleRegistersAsync(ushort address, ushort[] data, CancellationToken cancellationToken = default)` | Записывает несколько регистров (функция 0x10). |

Исключения:

- `ExecuteAsync` может бросить `ObjectDisposedException` (после `Dispose`) и
  `OperationCanceledException` при отмене; ошибки устройства/транспорта приходят
  в ответе.
- `EnsureConnectedAsync` бросает `MvkModbusException` (`DeviceDisabled`, если
  устройство отключено; `Transport`/`Timeout` при неудаче подключения).
- Обёртки чтения/записи бросают `MvkModbusException` из ответа
  (`throw response.Error`).

### 6.4. `Mvk.Modbus.Pool.MvkDevice`

Устройство пула: последовательно выполняет запросы через пайплайн, отслеживает
состояние, планирует реконнект и отключает простаивающее соединение. Класс `sealed`,
реализует `IMvkDevice`.

| Сигнатура | Описание |
|---|---|
| `MvkDevice(MvkDeviceSettings settings, IMvkTransport transport, MvkPipeline pipeline, ILogger<MvkDevice>? logger = null)` | Создаёт устройство пула. `settings`, `transport` или `pipeline` равны `null` — `ArgumentNullException`. Внутри создаёт `SemaphoreSlim` с счётчиком `Math.Max(1, settings.MaxConcurrentRequests)`. |
| `MvkDeviceSettings Settings { get; }` | Параметры устройства. |
| `MvkDeviceState State { get; }` | Текущее состояние (потокобезопасное чтение). |
| `bool IsConnected { get; }` | Делегируется транспорту (`transport.IsConnected`). |
| `MvkModbusException? LastError { get; }` | Последняя ошибка обращения. |
| `DateTimeOffset LastRequestAt { get; }` | Время последнего запроса (UTC) или `DateTimeOffset.MinValue`. |
| `event EventHandler<MvkDeviceStateChangedEventArgs>? StateChanged` | Событие изменения состояния. |
| `Task<ModbusResponse> ExecuteAsync(ModbusRequest request, CancellationToken cancellationToken = default)` | Выполняет запрос, бросает `ArgumentNullException` при `request == null` и `ObjectDisposedException` после `Dispose`. При `Settings.Enabled == false` возвращает ответ с ошибкой `DeviceDisabled` без обращения к пайплайну. |
| `Task<ModbusResponse> ExecuteAsync(ModbusRequest request, TimeSpan timeout, CancellationToken cancellationToken = default)` | То же, дополнительно кладёт `(int)timeout.TotalMilliseconds` в `context.Items[MvkContextItems.TimeoutMs]`. |
| `Task EnsureConnectedAsync(CancellationToken cancellationToken = default)` | Подключает устройство: `Connecting → Connected`; при `MvkModbusException` переводит в `Faulted` и пробрасывает, при отмене — в `Disconnected` и пробрасывает `OperationCanceledException`. |
| `Task DisconnectAsync(CancellationToken cancellationToken = default)` | Закрывает соединение и переводит устройство в `Disconnected`. |
| `Task<ushort[]> ReadHoldingRegistersAsync(ushort address, ushort quantity, CancellationToken cancellationToken = default)` | Чтение 0x03; при ошибке бросает `MvkModbusException`. |
| `Task<ushort[]> ReadInputRegistersAsync(ushort address, ushort quantity, CancellationToken cancellationToken = default)` | Чтение 0x04; при ошибке бросает `MvkModbusException`. |
| `Task WriteSingleRegisterAsync(ushort address, ushort value, CancellationToken cancellationToken = default)` | Запись 0x06; при ошибке бросает `MvkModbusException`. |
| `Task WriteMultipleRegistersAsync(ushort address, ushort[] data, CancellationToken cancellationToken = default)` | Запись 0x10; при ошибке бросает `MvkModbusException`. |
| `void Dispose()` | Идемпотентно освобождает транспорт и семафор. |

Примечания по поведению:

- Запросы к одному устройству сериализуются семафором `MaxConcurrentRequests`;
  ожидание захвата семафора учитывает `cancellationToken`.
- После каждого запроса обновляется `LastRequestAt`, а по завершении —
  внутренняя метка активности (используется для idle-отключения).
- Переходы состояний: успех — `Connected`/`Disconnected` (по состоянию
  транспорта), временная ошибка — `Faulted` с экспоненциальной задержкой
  реконнекта (`ReconnectBaseDelayMs * 2^(n-1)`, ограничение `ReconnectMaxDelayMs`),
  нетранзиентная ошибка — текущее состояние с записью `LastError`. Событие
  `StateChanged` вызывается только при фактической смене состояния.
- Метод `MaintainAsync(DateTimeOffset now, CancellationToken cancellationToken)`
  — `internal`; его вызывает пул (реконнект `Faulted`-устройств и закрытие
  простаивающих соединений). В публичный API не входит.

### 6.5. `Mvk.Modbus.Pool.IMvkDevicePool`

Пул устройств: хранение параметров, доступ по идентификатору, фоновый реконнект
и отключение простаивающих соединений. Наследует `IAsyncDisposable`.

| Сигнатура | Описание |
|---|---|
| `MvkModbusOptions Options { get; }` | Корневая конфигурация пула. |
| `IReadOnlyCollection<IMvkDevice> Devices { get; }` | Устройства пула. В реализации `MvkDevicePool` возвращается снимок списка. |
| `event EventHandler<MvkDeviceStateChangedEventArgs>? DeviceStateChanged` | Событие изменения состояния любого устройства пула. |
| `IMvkDevice GetDevice(string id)` | Возвращает устройство по идентификатору. Бросает `MvkModbusException` (`DeviceNotFound`), если устройство не найдено. |
| `bool TryGetDevice(string id, out IMvkDevice device)` | Пробует получить устройство по идентификатору. Пустой `id` — `ArgumentException`. |
| `IMvkDevice AddDevice(MvkDeviceOptions deviceOptions)` | Добавляет устройство в пул во время работы. Дубликат идентификатора или пустой `Id` — `MvkModbusException` (`Configuration`). |
| `bool RemoveDevice(string id)` | Удаляет устройство из пула и освобождает его ресурсы. `false`, если устройство не найдено. |
| `Task MaintainAsync(CancellationToken cancellationToken = default)` | Выполняет один цикл обслуживания: реконнект `Faulted`-устройств и отключение простаивающих. |

### 6.6. `Mvk.Modbus.Pool.MvkDevicePool`

Пул устройств: конструирование устройств из конфигурации, доступ по идентификатору,
фоновый реконнект и отключение простаивающих соединений. Класс `sealed`, реализует
`IMvkDevicePool` и `IDisposable`.

| Сигнатура | Описание |
|---|---|
| `MvkDevicePool(MvkModbusOptions options, IMvkTransportFactory? transportFactory = null, IEnumerable<IModbusMiddleware>? middlewares = null, ILoggerFactory? loggerFactory = null)` | Создаёт пул из конфигурации. `options == null` — `ArgumentNullException`. По умолчанию используются `NModbusTransportFactory` и `MvkPipelineFactory.CreateDefaultMiddlewares`. Все устройства из `options.ResolveDevices()` добавляются сразу; при `options.Pool.ReconnectEnabled` запускается фоновый цикл обслуживания. |
| `MvkModbusOptions Options { get; }` | Корневая конфигурация пула. |
| `IReadOnlyCollection<IMvkDevice> Devices { get; }` | Снимок списка устройств (создаётся при каждом обращении). |
| `event EventHandler<MvkDeviceStateChangedEventArgs>? DeviceStateChanged` | Событие изменения состояния любого устройства (проксирует `StateChanged` устройств). |
| `static MvkDevicePool FromJsonFile(string jsonFilePath, ILoggerFactory? loggerFactory = null)` | Создаёт пул из JSON-файла (секция `MvkModbus`); использует `MvkModbusOptionsLoader.LoadFromJsonFile`. |
| `IMvkDevice GetDevice(string id)` | Возвращает устройство по идентификатору или бросает `MvkModbusException` (`DeviceNotFound`). |
| `bool TryGetDevice(string id, out IMvkDevice device)` | Пробует получить устройство. `id` пуст или пробелы — `ArgumentException`. |
| `IMvkDevice AddDevice(MvkDeviceOptions deviceOptions)` | Добавляет устройство во время работы; применяет `Options.Defaults`. Пустой `Id` или дубликат — `MvkModbusException` (`Configuration`). `deviceOptions == null` — `ArgumentNullException`. |
| `bool RemoveDevice(string id)` | Удаляет устройство (отписывает событие, вызывает `Dispose`). `false`, если не найдено. |
| `Task MaintainAsync(CancellationToken cancellationToken = default)` | Один цикл обслуживания по снимку устройств. |
| `ValueTask DisposeAsync()` | Останавливает фоновый цикл (отмена и ожидание), освобождает все устройства и ресурсы. Идемпотентно. |
| `void Dispose()` | Синхронная обёртка над `DisposeAsync()` (для контейнеров, поддерживающих только синхронное освобождение служб). |

Особенности поведения:

- Фоновый цикл использует `PeriodicTimer` с интервалом
  `Math.Max(1, Options.Pool.HealthCheckIntervalSeconds)` секунд; ошибки одного
  цикла логируются как `Warning` и не останавливают цикл.
- При добавлении устройства в конструкторе дубликат идентификатора не считается
  ошибкой: `AddCore` возвращает уже существующее устройство (пул строится
  детерминированно). Явный вызов `AddDevice` дубликаты запрещает.
- Идентификаторы устройств сравниваются без учёта регистра
  (`ConcurrentDictionary` с `StringComparer.OrdinalIgnoreCase`).

```csharp
using Mvk.Modbus.Pool;

await using MvkDevicePool pool = MvkDevicePool.FromJsonFile("mvkmodbus.json");
IMvkDevice device = pool.GetDevice("mvk-1");
ushort[] registers = await device.ReadHoldingRegistersAsync(0x1004, 2);
```

---

## 7. Пространство имён Mvk.Modbus.Transport

Транспорт Modbus/TCP. NModbus используется только внутри этого пространства имён;
остальной код работает через `IMvkTransport`.

### 7.1. `Mvk.Modbus.Transport.IMvkTransport`

Транспорт соединения с одним Modbus-устройством. Наследует `IDisposable`.

| Сигнатура | Описание |
|---|---|
| `bool IsConnected { get; }` | Признак установленного соединения. |
| `Task ConnectAsync(CancellationToken cancellationToken)` | Устанавливает соединение с устройством. Бросает `MvkModbusException` (`Transport`/`Timeout`) при ошибке подключения. |
| `Task DisconnectAsync()` | Закрывает соединение с устройством. |
| `Task<ModbusResponse> ExecuteAsync(ModbusRequest request, CancellationToken cancellationToken)` | Выполняет запрос; при необходимости автоматически подключается. Транспортные ошибки возвращаются как `ModbusResponse.Failure`, отмена вызывающего кода — `OperationCanceledException`. |

### 7.2. `Mvk.Modbus.Transport.IMvkTransportFactory`

Фабрика транспортов: создаёт отдельный транспорт для каждого устройства пула.

| Сигнатура | Описание |
|---|---|
| `IMvkTransport Create(MvkDeviceSettings settings)` | Создаёт транспорт для устройства. |

### 7.3. `Mvk.Modbus.Transport.NModbusTransport`

Транспорт Modbus/TCP на базе NModbus с таймаутами из параметров устройства,
ленивым подключением и автоматическим разрывом при ошибке. Класс `sealed`,
реализует `IMvkTransport`.

| Сигнатура | Описание |
|---|---|
| `NModbusTransport(MvkDeviceSettings settings, ILogger<NModbusTransport>? logger = null)` | Создаёт транспорт для устройства. `settings == null` — `ArgumentNullException`; при `logger == null` используется `NullLogger<NModbusTransport>.Instance`. |
| `bool IsConnected { get; }` | `true`, если флаг соединения установлен и `TcpClient.Connected`. |
| `Task ConnectAsync(CancellationToken cancellationToken)` | Подключается к `settings.Host:settings.Port`; повторный вызов при активном соединении ничего не делает. |
| `Task DisconnectAsync()` | Закрывает соединение (мастер, сокет) и сбрасывает состояние. |
| `Task<ModbusResponse> ExecuteAsync(ModbusRequest request, CancellationToken cancellationToken)` | Выполняет операцию; при необходимости сначала подключается. |
| `void Dispose()` | Идемпотентно закрывает соединение и освобождает ресурсы. |

Детали `ConnectAsync`:

- Захватывает внутренний семафор (одно подключение за раз); ожидание учитывает
  `cancellationToken`.
- Таймаут подключения — `settings.ConnectTimeoutMs` (связанный CTS).
- `OperationCanceledException` при отменённом токене вызывающего кода
  пробрасывается; при срабатывании собственного таймаута бросается
  `MvkModbusException` с `MvkErrorKind.Timeout`.
- `SocketException` и прочие ошибки оборачиваются в `MvkModbusException`
  с `MvkErrorKind.Transport`.
- После подключения мастеру задаются `ReadTimeout = settings.ReadTimeoutMs` и
  `WriteTimeout = settings.WriteTimeoutMs`.

Детали `ExecuteAsync`:

- Бросает `ArgumentNullException` при `request == null`, `ObjectDisposedException`
  после `Dispose`, `OperationCanceledException` при отменённом токене.
- Поддерживаются все четыре операции: 0x03, 0x04, 0x06, 0x10. Для записи
  возвращается `ModbusResponse.WriteSuccess()`, для чтения — `ModbusResponse.Success(data)`
  с `settings.SlaveId`.
- При любой ошибке соединение закрывается, пишется предупреждение в лог и
  возвращается `ModbusResponse.Failure(...)`; отмена (`OperationCanceledException`)
  пробрасывается без оборачивания.
- Преобразование исключений NModbus/.NET в `MvkErrorKind` выполняет внутренний
  `internal static MvkModbusException MapException(MvkDeviceSettings settings, Exception exception)`
  (в публичный API не входит): `TimeoutException` и таймауты сокета — `Timeout`,
  `SlaveException` — `DeviceError` с кодом из ответа, прочее — `Transport`.

### 7.4. `Mvk.Modbus.Transport.NModbusTransportFactory`

Создаёт транспорты `NModbusTransport` для устройств пула. Класс `sealed`,
реализует `IMvkTransportFactory`.

| Сигнатура | Описание |
|---|---|
| `NModbusTransportFactory(ILoggerFactory? loggerFactory = null)` | Создаёт фабрику транспортов; при `null` используется `NullLoggerFactory.Instance`. |
| `IMvkTransport Create(MvkDeviceSettings settings)` | Создаёт `NModbusTransport` с логгером из фабрики. `settings == null` — `ArgumentNullException`. |

---

## 8. Пространство имён Mvk.Modbus.DependencyInjection

Регистрация библиотеки в DI-контейнере `Microsoft.Extensions.DependencyInjection`,
реестр и резолвер middleware, hosted-сервис прогрева.

### 8.1. `Mvk.Modbus.DependencyInjection.MvkModbusServiceCollectionExtensions`

Регистрация библиотеки в DI-контейнере. `static class`.

| Сигнатура | Описание |
|---|---|
| `const string DefaultSectionName = "MvkModbus"` | Имя секции конфигурации по умолчанию. |
| `static IServiceCollection AddMvkModbus(this IServiceCollection services, IConfiguration configuration, string sectionName = DefaultSectionName)` | Регистрирует пул устройств с привязкой секции конфигурации. `configuration == null` — `ArgumentNullException`. |
| `static IServiceCollection AddMvkModbus(this IServiceCollection services, Action<MvkModbusOptions> configure)` | Регистрирует пул устройств с программной настройкой. `configure == null` — `ArgumentNullException`. |
| `static IServiceCollection AddModbusMiddleware<TMiddleware>(this IServiceCollection services, string? name = null) where TMiddleware : class, IModbusMiddleware` | Регистрирует пользовательский middleware под именем для секции `Pipeline`; без имени используется имя типа (`typeof(TMiddleware).Name`). |
| `static IServiceCollection AddMvkModbusHostedService(this IServiceCollection services)` | Добавляет hosted-сервис, который при старте хоста инициализирует пул и пишет в лог список устройств. |

Что регистрирует `AddMvkModbus` (через внутренний `AddMvkModbusCore`):

| Регистрация | Время жизни | Примечание |
|---|---|---|
| `IOptions<MvkModbusOptions>` (`.Configure(configureOptions)`) | Options | Повторные вызовы накапливают действия настройки. |
| `ValidateOnStart()` | — | Валидация при старте хоста (иначе — при первом разрешении). |
| `IValidateOptions<MvkModbusOptions>` → `MvkModbusOptionsValidator` | Singleton | `TryAddEnumerable` — дубликаты не добавляются. |
| `IMvkTransportFactory` → `NModbusTransportFactory` | Singleton | `TryAddSingleton` — первая регистрация выигрывает. |
| `MvkDevicePool` | Singleton | Создаётся лениво; middleware разрешаются через `MvkMiddlewareResolver`. |
| `IMvkDevicePool` | Singleton | Возвращает тот же `MvkDevicePool`. |

Примечание: перегрузка `AddMvkModbus(IConfiguration, string)` только привязывает
секцию (`Bind`) и не проверяет её наличие — отсутствующая секция оставляет
значения по умолчанию, то есть валидный пул без устройств. Если секция обязана
существовать, используйте `MvkModbusOptionsLoader.LoadFromJsonFile`.

```csharp
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile("mvkmodbus.json", optional: false);

builder.Services.AddMvkModbus(builder.Configuration); // пул + конфигурация + валидация
builder.Services.AddModbusMiddleware<AuditMiddleware>("audit");
builder.Services.AddMvkModbusHostedService();

using IHost host = builder.Build();
await host.StartAsync();

IMvkDevicePool pool = host.Services.GetRequiredService<IMvkDevicePool>();
```

### 8.2. `Mvk.Modbus.DependencyInjection.MvkMiddlewareRegistry`

Реестр пользовательских middleware: имя из секции `Pipeline` → тип middleware.
Класс `sealed`; публичный конструктор — неявный. Имена сравниваются без учёта
регистра (`StringComparer.OrdinalIgnoreCase`).

| Сигнатура | Описание |
|---|---|
| `void Add(string name, Type middlewareType)` | Регистрирует тип middleware под именем. Пустое имя — `ArgumentException`; если тип не реализует `IModbusMiddleware` — `MvkModbusException` (`Configuration`). Имя обрезается (`Trim()`). |
| `bool TryGetType(string name, out Type middlewareType)` | Ищет тип middleware по имени; `false`, если имя не зарегистрировано. |

### 8.3. `Mvk.Modbus.DependencyInjection.MvkMiddlewareResolver`

Создаёт список middleware по именам из секции `Pipeline`: встроенные имена и
пользовательские из `MvkMiddlewareRegistry`. `static class`.

| Сигнатура | Описание |
|---|---|
| `static IReadOnlyList<IModbusMiddleware> ResolveMiddlewares(MvkModbusOptions options, IServiceProvider serviceProvider)` | Разрешает middleware в порядке `options.ResolvePipeline()`. `options == null` или `serviceProvider == null` — `ArgumentNullException`. |

Поведение:

- Имена из `MvkMiddlewareNames` (без учёта регистра) создаются напрямую:
  `timeout`, `logging` (с логгером из DI при наличии), `metrics`, `validation`,
  `retry`, `writeProtection`.
- Прочие имена ищутся в `MvkMiddlewareRegistry`; экземпляр создаётся через
  `ActivatorUtilities.CreateInstance(serviceProvider, customType)` — зависимости
  конструктора разрешаются контейнером.
- Если имя не найдено ни среди встроенных, ни в реестре — `MvkModbusException`
  (`Configuration`).

### 8.4. `Mvk.Modbus.DependencyInjection.MvkModbusWarmupHostedService`

При старте хоста инициализирует пул устройств (проверка конфигурации) и логирует
состав пула. Класс `sealed`, реализует `IHostedService`.

| Сигнатура | Описание |
|---|---|
| `MvkModbusWarmupHostedService(IMvkDevicePool pool, ILogger<MvkModbusWarmupHostedService> logger)` | Создаёт hosted-сервис. `pool == null` или `logger == null` — `ArgumentNullException`. |
| `Task StartAsync(CancellationToken cancellationToken)` | Логирует `Пул МВК инициализирован: устройств N (...)`; соединения с устройствами не устанавливаются (подключение ленивое). |
| `Task StopAsync(CancellationToken cancellationToken)` | Ничего не делает; освобождение пула выполняет DI-контейнер. |

---

## 9. Карта типов по пространствам имён

| Пространство имён | Публичные типы |
|---|---|
| `Mvk.Modbus` | `MvkErrorKind`, `MvkModbusException` |
| `Mvk.Modbus.Configuration` | `MvkByteOrder`, `MvkWritePolicy`, `MvkDefaultsOptions`, `MvkDeviceOptions`, `MvkDeviceSettings`, `MvkPoolOptions`, `MvkModbusOptions`, `MvkModbusOptionsValidator`, `MvkModbusOptionsLoader` |
| `Mvk.Modbus.Protocol` | `ModbusOperation`, `ModbusRequest`, `ModbusResponse`, `RegisterCodec` |
| `Mvk.Modbus.Pipeline` | `InvokeModbusDelegate`, `IModbusMiddleware`, `ModbusRequestContext`, `MvkContextItems`, `MvkMiddlewareNames`, `MvkPipelineBuilder`, `MvkPipeline`, `MvkPipelineFactory` |
| `Mvk.Modbus.Pipeline.Middlewares` | `TimeoutMiddleware`, `LoggingMiddleware`, `MetricsMiddleware`, `ValidationMiddleware`, `RetryMiddleware`, `WriteProtectionMiddleware` |
| `Mvk.Modbus.Pool` | `MvkDeviceState`, `MvkDeviceStateChangedEventArgs`, `IMvkDevice`, `MvkDevice`, `IMvkDevicePool`, `MvkDevicePool` |
| `Mvk.Modbus.Transport` | `IMvkTransport`, `IMvkTransportFactory`, `NModbusTransport`, `NModbusTransportFactory` |
| `Mvk.Modbus.DependencyInjection` | `MvkModbusServiceCollectionExtensions`, `MvkMiddlewareRegistry`, `MvkMiddlewareResolver`, `MvkModbusWarmupHostedService` |

Типовые сценарии и примеры использования разобраны в главах 03–06; типизированный
слой МВК01 (`Mvk.MVK01`) — в справочнике 12b.

---

## См. также

- [Справочник Mvk.MVK01](12b-api-reference-mvk-mvk01.md)
- [README](../README.md)
- [Глава 03. Конфигурация](03-configuration.md)
- [Глава 04. Транспорт и сырые регистры](04-transport-and-raw-registers.md)
- [Глава 05. Пул устройств](05-device-pool.md)
- [Глава 06. Пайплайн middleware](06-pipeline-middleware.md)
