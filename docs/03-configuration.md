# Глава 03. Конфигурация

Всё, что библиотека знает о подключаемых устройствах — адреса, порты, таймауты,
ретраи, политика записи, состав пула — задаётся секцией `MvkModbus`. Эта глава
описывает модель конфигурации, каждое свойство с точным значением по умолчанию,
валидацию и все способы загрузки: из JSON-файла, из `IConfiguration` (appsettings,
переменные окружения) и программно.

Материал опирается на исходники `src/Mvk.Modbus/Configuration/*`,
`src/Mvk.Modbus/DependencyInjection/*` и тесты `tests/Mvk.Modbus.Tests/Configuration/*`;
все сигнатуры и значения по умолчанию сверены с кодом.

Содержание:

1. [Общая модель](#1-общая-модель)
2. [MvkModbusOptions](#2-mvkmodbusoptions)
3. [MvkDefaultsOptions](#3-mvkdefaultsoptions)
4. [MvkDeviceOptions](#4-mvkdeviceoptions)
5. [MvkDeviceSettings](#5-mvkdevicesettings)
6. [MvkPoolOptions](#6-mvkpooloptions)
7. [MvkWritePolicy](#7-mvkwritePolicy)
8. [MvkByteOrder](#8-mvkbyteorder)
9. [Валидация](#9-валидация)
10. [MvkModbusOptionsLoader](#10-mvkmodbusoptionsloader)
11. [Полные примеры конфигов](#11-полные-примеры-конфигов)
12. [Практические советы](#12-практические-советы)
13. [Что будет, если](#13-что-будет-если)
14. [Подводные камни](#подводные-камни)
15. [См. также](#см-также)

---

## 1. Общая модель

Корень конфигурации — секция `MvkModbus` (константа `DefaultSectionName` есть
и в `MvkModbusServiceCollectionExtensions`, и в `MvkModbusOptionsLoader`).
Внутри — четыре узла:

```json
{
  "MvkModbus": {
    "Defaults": { },
    "Pool": { },
    "Pipeline": [ ],
    "Devices": [ ]
  }
}
```

| Узел | Тип | Назначение |
|---|---|---|
| `Defaults` | `MvkDefaultsOptions` | Значения по умолчанию для всех устройств пула |
| `Pool` | `MvkPoolOptions` | Фоновое обслуживание пула (реконнект, простой) |
| `Pipeline` | `List<string>?` | Имена и порядок middleware; `null` — порядок по умолчанию |
| `Devices` | `List<MvkDeviceOptions>?` | Список устройств; `null` — пустой пул |

Модель наследования простая: устройство **не** хранит полный набор параметров,
а лишь переопределения. При создании пула (`MvkDevicePool`) и при валидации для
каждого элемента `Devices` вызывается `MvkDeviceOptions.Resolve(Defaults)`, который
подставляет недостающие значения из `Defaults`.

Переопределяются на уровне устройства только эти поля:

| Поле устройства | Что переопределяет |
|---|---|
| `Port` | `Defaults.Port` |
| `SlaveId` | `Defaults.SlaveId` |
| `ByteOrder` | `Defaults.ByteOrder` |
| `MaxConcurrentRequests` | `Defaults.MaxConcurrentRequests` |
| `AllowConfigurationWrite` | `Defaults.AllowConfigurationWrite` |

Всё остальное — таймауты, ретраи, задержки реконнекта, idle-отключение,
`WritePolicy`, защищённая область, `RequireEvenAddressAndQuantity` — задаётся
**только** в `Defaults` и одинаково для всех устройств (см. разделы 3 и 4).

### Где лежит файл

Библиотека не диктует расположение файла: она работает с любым `IConfiguration`.
В примерах (`samples/Mvk.Sample.Console`) файл `mvkmodbus.json` копируется
в выходной каталог и подключается так:

```csharp
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "mvkmodbus.json"),
    optional: false,
    reloadOnChange: false);

builder.Services.AddMvkModbus(builder.Configuration);
```

В `.csproj` файл помечается как копируемый:

```xml
<ItemGroup>
  <None Update="mvkmodbus.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

### Варианты подключения

**1. DI + отдельный JSON-файл** (рекомендуется для приложений):

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Mvk.Modbus.DependencyInjection;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile("mvkmodbus.json", optional: false, reloadOnChange: false);

builder.Services.AddMvkModbus(builder.Configuration);   // секция MvkModbus
builder.Services.AddMvkModbusHostedService();            // лог состава пула при старте
```

**2. Секция внутри appsettings.json** — тот же `AddMvkModbus`, но файл уже
подключён хостом; можно указать другое имя секции:

```csharp
builder.Services.AddMvkModbus(builder.Configuration);                      // "MvkModbus"
builder.Services.AddMvkModbus(builder.Configuration, "MvkModbusProduction"); // своя секция
```

**3. Программная настройка** — без файлов вообще:

```csharp
builder.Services.AddMvkModbus(options =>
{
    options.Defaults.RequireEvenAddressAndQuantity = true;
    options.Devices =
    [
        new MvkDeviceOptions { Id = "mvk-1", Host = "10.20.0.211" }
    ];
});
```

**4. Без DI** — автономный загрузчик и пул:

```csharp
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Pool;

MvkModbusOptions options = MvkModbusOptionsLoader.LoadFromJsonFile("mvkmodbus.json");
await using MvkDevicePool pool = new(options);
// либо одной строкой:
await using MvkDevicePool pool2 = MvkDevicePool.FromJsonFile("mvkmodbus.json");
```

Порядок регистраций в DI важен: сначала `AddMvkModbus`, затем `AddMvk01Devices`
(типизированный слой МВК01), затем `AddMvkModbusHostedService` и
`AddModbusMiddleware<T>("имя")` (см. главу о пайплайне).

Конфигурация привязывается один раз: `IOptions<MvkModbusOptions>` (не
`IOptionsMonitor`), а пул создаёт устройства при своём конструировании. Изменение
файла на диске после старта на состав пула и параметры устройств не влияет —
`reloadOnChange: true` сам по себе горячего применения не даёт.

## 2. MvkModbusOptions

Корневой класс секции (`namespace Mvk.Modbus.Configuration`):

```csharp
public sealed class MvkModbusOptions
{
    public MvkDefaultsOptions Defaults { get; set; } = new();
    public MvkPoolOptions Pool { get; set; } = new();
    public List<string>? Pipeline { get; set; }
    public List<MvkDeviceOptions>? Devices { get; set; }
}
```

| Свойство | Тип | По умолчанию | Описание |
|---|---|---|---|
| `Defaults` | `MvkDefaultsOptions` | новый экземпляр | Общие значения для всех устройств |
| `Pool` | `MvkPoolOptions` | новый экземпляр | Настройки фонового обслуживания пула |
| `Pipeline` | `List<string>?` | `null` | Порядок middleware (первый — внешний); `null` — порядок по умолчанию |
| `Devices` | `List<MvkDeviceOptions>?` | `null` | Устройства пула; `null` — пул без устройств |

Методы:

| Метод | Сигнатура | Поведение |
|---|---|---|
| `ResolvePipeline` | `IReadOnlyList<string> ResolvePipeline()` | Возвращает `Pipeline`, а если он `null` — `CreateDefaultPipeline()` |
| `ResolveDevices` | `IReadOnlyList<MvkDeviceSettings> ResolveDevices()` | Применяет `Defaults` к каждому устройству; при `Devices == null` — пустой список |
| `CreateDefaultPipeline` | `static List<string> CreateDefaultPipeline()` | Новый список `timeout, logging, metrics, validation, retry, writeProtection` |

Примеры:

```csharp
MvkModbusOptions options = new();

// Pipeline не задан -> порядок по умолчанию.
IReadOnlyList<string> order = options.ResolvePipeline();
// ["timeout", "logging", "metrics", "validation", "retry", "writeProtection"]

// Программно заданный пустой пайплайн.
options.Pipeline = [];
bool isEmpty = options.ResolvePipeline().Count == 0;   // true

// Устройств нет -> пустой список, исключений нет.
bool noDevices = options.ResolveDevices().Count == 0;  // true
```

Порядок по умолчанию: `timeout` → `logging` → `metrics` → `validation` →
`retry` → `writeProtection`. Первым в списке идёт самый внешний middleware.
Пустой **программный** список (`Pipeline = []`) означает «без middleware вообще»:
ни проверок, ни ретраев, ни защиты записи (см. раздел 13).

## 3. MvkDefaultsOptions

`MvkDefaultsOptions` — 17 свойств, которые наследуют все устройства.

### Полная таблица

| Свойство | Тип | По умолчанию | Допустимый диапазон | Влияние |
|---|---|---|---|---|
| `Port` | `int` | `502` | 1–65535 | TCP-порт Modbus |
| `SlaveId` | `byte` | `1` | 1–247 | Идентификатор устройства Modbus |
| `ByteOrder` | `MvkByteOrder` | `BigEndian` | значения перечисления | Порядок байт 32-битных значений |
| `ConnectTimeoutMs` | `int` | `3000` | 1–600000 | Таймаут установления TCP-соединения |
| `ReadTimeoutMs` | `int` | `1000` | 1–600000 | Таймаут ожидания ответа на чтение |
| `WriteTimeoutMs` | `int` | `3000` | 1–600000 | Таймаут ожидания ответа на запись |
| `MaxRetries` | `int` | `2` | 0–10 | Число повторных попыток при временных ошибках |
| `RetryDelayMs` | `int` | `150` | 0–60000 | Базовая задержка между попытками (линейный рост) |
| `ReconnectBaseDelayMs` | `int` | `500` | 1–600000 | Базовая задержка реконнекта |
| `ReconnectMaxDelayMs` | `int` | `30000` | 1–3600000, не меньше `ReconnectBaseDelayMs` | Потолок экспоненциального backoff |
| `IdleDisconnectSeconds` | `int` | `120` | 0–86400 | Простой до закрытия соединения; 0 — не закрывать |
| `MaxConcurrentRequests` | `int` | `1` | 1–64 | Одновременных запросов к одному устройству |
| `WritePolicy` | `MvkWritePolicy` | `ProtectConfigArea` | значения перечисления | Политика записи |
| `AllowConfigurationWrite` | `bool` | `false` | — | Разрешение записи в защищённую область |
| `ProtectedWriteStartAddress` | `ushort` | `0x0000` | 0–0xFFFF | Начало защищённой области (включительно) |
| `ProtectedWriteEndAddress` | `ushort` | `0x0FFF` | 0–0xFFFF, не меньше начала | Конец защищённой области (включительно) |
| `RequireEvenAddressAndQuantity` | `bool` | `false` | — | Требовать чётные адрес и число регистров |

Диапазоны — из `MvkModbusOptionsValidator`; при выходе за них конфигурация
не проходит валидацию (раздел 9). Для 16-битных полей `ushort` диапазон
0–65535 ограничен самим типом: значение больше 65535 биндер не преобразует
(исключение `InvalidOperationException`).

### Подключение

- **`Port`** — порт Modbus/TCP. Стандарт для МВК01 — 502; нестандартный порт
  задаётся, если устройство опубликовано через проброс (например, 10502).
- **`SlaveId`** — unit id в кадре Modbus. Тип `byte`; допустимы 1–247.
- **`ByteOrder`** — подробно в разделе 8.

### Таймауты

- **`ConnectTimeoutMs`** — ограничивает `TcpClient.ConnectAsync`; по истечении
  выбрасывается `MvkModbusException` (`Timeout`) с текстом
  «Таймаут подключения к {Host}:{Port} ({...} мс)».
- **`ReadTimeoutMs` / `WriteTimeoutMs`** — выставляются на транспорт NModbus
  (`ReadTimeout`/`WriteTimeout`). Это таймауты ожидания ответа после отправки
  запроса, а не общее время операции.
- Общий таймаут операции (`TimeoutMiddleware`) включается только для вызовов
  с явным лимитом (например, системные команды МВК01) и в конфигурации
  устройства не задаётся.

### Ретраи

- **`MaxRetries`** — число *повторов*; всего попыток `MaxRetries + 1`.
  Повторяются только временные ошибки (`IsTransient`: `Transport`, `Timeout`,
  `Busy`). `MaxRetries = 0` — одна попытка без повторов.
- **`RetryDelayMs`** — задержка перед попыткой номер `N` равна
  `RetryDelayMs × N` (линейно): при 150 мс — 150, 300, 450… Пауза `0` отключает
  ожидание.

### Реконнект

- **`ReconnectBaseDelayMs`**, **`ReconnectMaxDelayMs`** — экспоненциальный
  backoff после транспортной ошибки: задержка до следующего подключения равна
  `min(Base × 2^(число неудач − 1), Max)`. При базе 500 мс и потолке 30 с
  последовательность: 500 мс, 1 с, 2 с, 4 с, 8 с, 16 с, 30 с, 30 с…
  Успешный запрос сбрасывает счётчик неудач.
- **`IdleDisconnectSeconds`** — если соединение не использовалось дольше
  указанного времени, фоновый цикл его закрывает. `0` — не закрывать никогда.
  Проверка выполняется в цикле обслуживания пула (раздел 6).

### Одновременность

- **`MaxConcurrentRequests`** — размер семафора `MvkDevice`: столько запросов
  к одному устройству могут выполняться одновременно. Значение по умолчанию 1
  сериализует все обращения; увеличивать следует осознанно (раздел 12).

### Запись

- **`WritePolicy`**, **`AllowConfigurationWrite`**,
  **`ProtectedWriteStartAddress`**, **`ProtectedWriteEndAddress`** — раздел 7.

### Особенность МВК01

- **`RequireEvenAddressAndQuantity`** — при `true` middleware `validation`
  отклоняет запросы с нечётным адресом или нечётным числом регистров
  (`MvkErrorKind.Validation`). Для МВК01 это обязательно: устройство принимает
  только 32-битные пары. `AddMvk01Devices()` принудительно ставит `true`
  в `Defaults` (см. раздел 9).

### Пример

```json
{
  "MvkModbus": {
    "Defaults": {
      "Port": 502,
      "SlaveId": 1,
      "ByteOrder": "BigEndian",
      "ConnectTimeoutMs": 3000,
      "ReadTimeoutMs": 2000,
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
    }
  }
}
```

## 4. MvkDeviceOptions

Один элемент списка `Devices`.

| Свойство | Тип | По умолчанию | Обязательное | Описание |
|---|---|---|---|---|
| `Id` | `string` | `""` | да | Уникальный идентификатор в пуле |
| `Name` | `string?` | `null` | нет | Отображаемое имя |
| `Host` | `string` | `""` | да | IP-адрес или DNS-имя |
| `Port` | `int?` | `null` | нет | Переопределяет `Defaults.Port` |
| `SlaveId` | `byte?` | `null` | нет | Переопределяет `Defaults.SlaveId` |
| `ByteOrder` | `MvkByteOrder?` | `null` | нет | Переопределяет `Defaults.ByteOrder` |
| `Enabled` | `bool` | `true` | нет | Выключенное устройство отвечает `DeviceDisabled` |
| `MaxConcurrentRequests` | `int?` | `null` | нет | Переопределяет `Defaults.MaxConcurrentRequests` |
| `AllowConfigurationWrite` | `bool?` | `null` | нет | Переопределяет `Defaults.AllowConfigurationWrite` |
| `Tags` | `Dictionary<string,string>?` | `null` | нет | Произвольные метки устройства |

### Метод Resolve

```csharp
public MvkDeviceSettings Resolve(MvkDefaultsOptions defaults)
```

Что делает `Resolve`:

1. Проверяет, что `defaults` не `null` (`ArgumentNullException`).
2. Копирует `Tags` в новый словарь с компаратором `StringComparer.OrdinalIgnoreCase`;
   если `Tags == null`, создаёт пустой словарь. Исходный словарь не изменяется.
3. Обрезает пробелы у `Id` и `Host` (`Trim`).
4. Подставляет значения по умолчанию для `Port`, `SlaveId`, `ByteOrder`,
   `MaxConcurrentRequests`, `AllowConfigurationWrite`, если поля устройства `null`.
5. Переносит без изменений все «общие» параметры из `Defaults`.
6. Возвращает `MvkDeviceSettings`.

Важная деталь: `Enabled` не наследуется — по умолчанию включён, выключение
задаётся явно (`"Enabled": false`). Идентификатор `Id` не может быть `null`:
устройство без `Id` не проходит валидацию.

### Пример per-device переопределений

```json
{
  "MvkModbus": {
    "Defaults": {
      "Port": 502,
      "SlaveId": 1,
      "ByteOrder": "BigEndian",
      "RequireEvenAddressAndQuantity": true
    },
    "Devices": [
      {
        "Id": "mvk-1",
        "Name": "МВК01 стенд",
        "Host": "10.20.0.211",
        "Tags": { "asset": "Стенд", "location": "Лаборатория" }
      },
      {
        "Id": "vdm-82",
        "Name": "ВДМ82",
        "Host": "10.20.0.220",
        "Port": 10502,
        "SlaveId": 3,
        "ByteOrder": "Mixed",
        "MaxConcurrentRequests": 1
      }
    ]
  }
}
```

Здесь первое устройство целиком наследует `Defaults`, второе — переопределяет
порт, SlaveId и порядок байт.

То же программно:

```csharp
MvkDefaultsOptions defaults = new() { RequireEvenAddressAndQuantity = true };

MvkDeviceOptions device = new()
{
    Id = "vdm-82",
    Host = "10.20.0.220",
    Port = 10502,
    SlaveId = 3,
    ByteOrder = MvkByteOrder.Mixed,
    Tags = new Dictionary<string, string> { ["asset"] = "ВДМ82" }
};

MvkDeviceSettings settings = device.Resolve(defaults);
```

## 5. MvkDeviceSettings

`MvkDeviceSettings` — неизменяемый результат наследования: `sealed record`
со свойствами `required { get; init; }`. После создания пула настройки
устройства доступны через `IMvkDevice.Settings`.

| Поле | Тип | Значение |
|---|---|---|
| `Id` | `string` | Идентификатор (обрезан) |
| `Name` | `string?` | Отображаемое имя |
| `Host` | `string` | Адрес (обрезан) |
| `Port` | `int` | Итоговый порт |
| `SlaveId` | `byte` | Итоговый SlaveId |
| `ByteOrder` | `MvkByteOrder` | Итоговый порядок байт |
| `Enabled` | `bool` | Устройство в работе |
| `MaxConcurrentRequests` | `int` | Итоговый лимит одновременных запросов |
| `AllowConfigurationWrite` | `bool` | Итоговое разрешение записи конфигурации |
| `ConnectTimeoutMs` | `int` | Таймаут подключения, мс |
| `ReadTimeoutMs` | `int` | Таймаут чтения, мс |
| `WriteTimeoutMs` | `int` | Таймаут записи, мс |
| `MaxRetries` | `int` | Число повторов |
| `RetryDelayMs` | `int` | Базовая задержка повторов, мс |
| `ReconnectBaseDelayMs` | `int` | Базовая задержка реконнекта, мс |
| `ReconnectMaxDelayMs` | `int` | Потолок задержки реконнекта, мс |
| `IdleDisconnectSeconds` | `int` | Простой до отключения, с |
| `WritePolicy` | `MvkWritePolicy` | Политика записи |
| `ProtectedWriteStartAddress` | `ushort` | Начало защищённой области |
| `ProtectedWriteEndAddress` | `ushort` | Конец защищённой области |
| `RequireEvenAddressAndQuantity` | `bool` | Требование чётности |
| `Tags` | `IReadOnlyDictionary<string,string>` | Метки (не `null`, регистронезависимые) |
| `Endpoint` | `string` (вычисляемое) | `$"{Host}:{Port}"` |

Пример:

```csharp
IMvkDevice device = pool.GetDevice("mvk-1");

Console.WriteLine(device.Settings.Endpoint);          // 10.20.0.211:502
Console.WriteLine(device.Settings.RequireEvenAddressAndQuantity); // true

// Поиск метки регистронезависим: ключи "asset" и "ASSET" равнозначны.
string asset = device.Settings.Tags["ASSET"];
```

`Tags` всегда не `null`: если метки не заданы, словарь пуст. Сравнение ключей —
`OrdinalIgnoreCase`, поэтому `"asset"`, `"Asset"` и `"ASSET"` — один ключ.

## 6. MvkPoolOptions

Настройки фонового обслуживания пула (общие для всех устройств).

| Свойство | Тип | По умолчанию | Описание |
|---|---|---|---|
| `ReconnectEnabled` | `bool` | `true` | Включить фоновый цикл обслуживания |
| `HealthCheckIntervalSeconds` | `int` | `15` | Период цикла, с (эффективный минимум — 1) |

Что делает фоновый цикл (`MvkDevicePool`):

- при `ReconnectEnabled = true` в конструкторе пула запускается задача
  с `PeriodicTimer`; период — `Math.Max(1, HealthCheckIntervalSeconds)`;
- на каждом тике вызывается `MaintainAsync`, который обходит все устройства
  и для каждого `MvkDevice`:
  - если устройство в состоянии `Faulted` и наступила рассчитанная задержка —
    выполняет `EnsureConnectedAsync` (реконнект); успех логируется,
    неудача — в debug-лог, цикл продолжается;
  - иначе, если соединение открыто и простой превысил `IdleDisconnectSeconds`, —
    закрывает соединение;
  - ошибки обслуживания одного тика перехватываются и логируются как warning,
    цикл не останавливается.
- при `ReconnectEnabled = false` цикл не запускается: реконнект и idle-отключение
  автоматически не выполняются. Их можно запускать вручную вызовом
  `pool.MaintainAsync(ct)`; обычные запросы всё равно пытаются подключиться
  лениво при необходимости.

Пример:

```json
{
  "MvkModbus": {
    "Pool": {
      "ReconnectEnabled": true,
      "HealthCheckIntervalSeconds": 15
    }
  }
}
```

Секция `Pool` не проверяется валидатором: значения вне здравого смысла не
являются ошибкой конфигурации. `HealthCheckIntervalSeconds = 0` или
отрицательное значение даст период 1 с.

## 7. MvkWritePolicy

`MvkWritePolicy` — политика записи, применяемая middleware `writeProtection`
(если он присутствует в `Pipeline`). Чтение политикой не ограничивается никогда.

| Значение | Число | Смысл |
|---|---|---|
| `AllowAll` | 0 | Запись разрешена во все регистры |
| `ProtectConfigArea` | 1 | Запись запрещена в защищённую область; остальное разрешено |
| `ReadOnly` | 2 | Запись запрещена полностью |

Алгоритм проверки запроса на запись (`WriteProtectionMiddleware.Check`):

1. Если запрос не является записью — проверок нет.
2. `WritePolicy == ReadOnly` — отказ:
   «Устройство '{Id}' доступно только для чтения (WritePolicy=ReadOnly).»
3. `WritePolicy == ProtectConfigArea` **и** `AllowConfigurationWrite == false`
   **и** диапазон запроса пересекается с защищённой областью — отказ:
   «Запись в защищённую область конфигурации (0xSTART–0xEND) устройства
   '{Id}' запрещена. Разрешите AllowConfigurationWrite.»
4. Иначе запись пропускается дальше.

Проверка перекрытия:

```csharp
request.StartAddress <= settings.ProtectedWriteEndAddress
    && request.EndAddress >= settings.ProtectedWriteStartAddress
```

То есть достаточно, чтобы запрос затронул **любой** регистр защищённой области.
Границы включительные; по умолчанию защищены адреса `0x0000–0x0FFF` — вся
конфигурация МВК01.

Взаимодействие параметров:

| `WritePolicy` | `AllowConfigurationWrite` | Результат |
|---|---|---|
| `AllowAll` | не важен | Любая запись разрешена |
| `ProtectConfigArea` | `false` | Запись в `Protected*Address` запрещена, вне её — разрешена |
| `ProtectConfigArea` | `true` | Запись разрешена везде, включая область конфигурации |
| `ReadOnly` | не важен | Любая запись запрещена |

Важные детали:

- `AllowConfigurationWrite` — nullable-поле устройства: можно снять защиту
  для одного устройства, не меняя общий `Defaults`.
- `ProtectedWriteStartAddress`/`ProtectedWriteEndAddress` устройство
  переопределить не может — только `Defaults`.
- Проверка выполняется только при наличии middleware `writeProtection`
  в `Pipeline`. При пустом пайплайне защиты нет (см. раздел 13).
- Нарушение политики возвращается как ответ с ошибкой `MvkErrorKind.Validation`
  (свойство `Error` у `ModbusResponse`); удобные обёртки
  (`WriteSingleRegisterAsync` и т.п.) выбрасывают `MvkModbusException`.
- `AddMvk01Devices()` в DI принудительно включает
  `Defaults.AllowConfigurationWrite = true` и
  `Defaults.RequireEvenAddressAndQuantity = true` на этапе `PostConfigure`.
  Значение можно сузить на уровне устройства: `"AllowConfigurationWrite": false`.

Примеры.

Только чтение:

```json
{
  "MvkModbus": {
    "Defaults": { "WritePolicy": "ReadOnly" },
    "Devices": [ { "Id": "mvk-1", "Host": "10.20.0.211" } ]
  }
}
```

Защита области (по умолчанию):

```json
{
  "MvkModbus": {
    "Defaults": {
      "WritePolicy": "ProtectConfigArea",
      "AllowConfigurationWrite": false,
      "ProtectedWriteStartAddress": "0x0000",
      "ProtectedWriteEndAddress": "0x0FFF"
    }
  }
}
```

Запись разрешена всем устройствам, кроме одного:

```json
{
  "MvkModbus": {
    "Defaults": {
      "WritePolicy": "ProtectConfigArea",
      "AllowConfigurationWrite": true
    },
    "Devices": [
      { "Id": "mvk-1", "Host": "10.20.0.211" },
      { "Id": "mvk-2", "Host": "10.20.0.212", "AllowConfigurationWrite": false }
    ]
  }
}
```

Программная проверка без выполнения запроса:

```csharp
using Mvk.Modbus.Pipeline.Middlewares;

MvkModbusException? denial = WriteProtectionMiddleware.Check(context);
```

## 8. MvkByteOrder

`MvkByteOrder` задаёт интерпретацию 32-битных значений (`float`, `uint`, `int`),
занимающих два 16-битных регистра. Он должен совпадать с настройкой самого
устройства — регистром `0x000C` МВК01 (`Mvk01RegisterMap.ByteOrder`).

| Значение | Число | Смысл | Регистр 0x000C |
|---|---|---|---|
| `BigEndian` | 0 | Старшее слово первым (по умолчанию) | 0x00000000 |
| `LittleEndian` | 1 | Полный обратный порядок байт | 0x66666666 |
| `Mixed` | 2 | Слова меняются местами (для ВДМ82) | 0x99999999 |

Порядок слов и байт (для `1.0f` = 0x3F800000, байты на проводе):

```
BigEndian:    байты 3F 80 00 00  -> регистры [0x3F80, 0x0000]
Mixed:        байты 00 00 3F 80  -> регистры [0x0000, 0x3F80]
LittleEndian: байты 00 00 80 3F  -> регистры [0x0000, 0x803F]
```

Эталонный пример `1.0f` (0x3F800000), совпадает с тестами `RegisterCodecTests`:

| Значение | BigEndian | Mixed | LittleEndian |
|---|---|---|---|
| `1.0f` | `0x3F80, 0x0000` | `0x0000, 0x3F80` | `0x0000, 0x803F` |
| `12.34f` | `0x4145, 0x70A4` | `0x70A4, 0x4145` | `0xA470, 0x4541` |

`ByteOrder` — одно из полей, переопределяемых на уровне устройства:

```json
{
  "MvkModbus": {
    "Defaults": { "ByteOrder": "BigEndian" },
    "Devices": [
      { "Id": "mvk-1", "Host": "10.20.0.211" },
      { "Id": "vdm-82", "Host": "10.20.0.220", "ByteOrder": "Mixed" }
    ]
  }
}
```

Практика: перед изменением `ByteOrder` проверьте регистр `0x000C` устройства
(или прочитайте значение через типизированный слой). Если библиотека считает
`BigEndian`, а устройство настроено на `Mixed`, все 32-битные значения будут
расшифрованы неверно — ошибки транспорта при этом не будет. Для МВК01
по умолчанию используется `BigEndian`; `Mixed` — типовой режим ВДМ82.

## 9. Валидация

Валидатор `MvkModbusOptionsValidator : IValidateOptions<MvkModbusOptions>`
проверяет диапазоны, обязательные поля и уникальность идентификаторов.

### Когда выполняется

| Сценарий | Момент проверки | Что получает вызывающий код |
|---|---|---|
| `AddMvkModbus(...)` + хост (`ValidateOnStart`) | `host.Start()` / `StartAsync()` | `OptionsValidationException` |
| `AddMvkModbus(...)` без старта хоста | Первое обращение к `IOptions<MvkModbusOptions>.Value` (создание пула) | `OptionsValidationException` |
| `MvkModbusOptionsLoader.LoadFromJsonFile` / `LoadFromConfiguration` | Сразу при загрузке | `MvkModbusException` с `MvkErrorKind.Configuration` |
| `AddMvkModbus(config)` при отсутствующей секции | — | Ошибки нет, пул создаётся без устройств |

В DI валидатор регистрируется через `TryAddEnumerable`, а `ValidateOnStart()`
подключается в `AddMvkModbusCore`. Поэтому при использовании хоста ошибка
конфигурации обнаруживается ещё до обработки первого запроса.

### Правила для Defaults

| Путь | Диапазон / условие |
|---|---|
| `Defaults.Port` | 1–65535 |
| `Defaults.SlaveId` | 1–247 |
| `Defaults.ConnectTimeoutMs` | 1–600000 |
| `Defaults.ReadTimeoutMs` | 1–600000 |
| `Defaults.WriteTimeoutMs` | 1–600000 |
| `Defaults.MaxRetries` | 0–10 |
| `Defaults.RetryDelayMs` | 0–60000 |
| `Defaults.ReconnectBaseDelayMs` | 1–600000 |
| `Defaults.ReconnectMaxDelayMs` | 1–3600000 и не меньше `ReconnectBaseDelayMs` |
| `Defaults.IdleDisconnectSeconds` | 0–86400 |
| `Defaults.MaxConcurrentRequests` | 1–64 |
| `Defaults.ProtectedWriteEndAddress` | не меньше `ProtectedWriteStartAddress` |

### Правила для устройств

Устройство проверяется **после** `Resolve(Defaults)`, то есть наследованные
значения тоже валидируются, но уже под именем устройства:

| Путь | Диапазон / условие |
|---|---|
| `Id` | Не пустой после `Trim` |
| `Id` | Уникален без учёта регистра |
| `Host` | Не пустой и не из пробелов |
| `Устройство '{id}'.Port` | 1–65535 |
| `Устройство '{id}'.SlaveId` | 1–247 |
| `Устройство '{id}'.MaxConcurrentRequests` | 1–64 |
| `Устройство '{id}'.ConnectTimeoutMs` | 1–600000 |
| `Устройство '{id}'.ReadTimeoutMs` | 1–600000 |
| `Устройство '{id}'.WriteTimeoutMs` | 1–600000 |
| `Устройство '{id}'.MaxRetries` | 0–10 |
| `Устройство '{id}'.IdleDisconnectSeconds` | 0–86400 |

### Точные тексты сообщений

| Ситуация | Сообщение |
|---|---|
| `options == null` (прямой вызов `Validate`) | `Секция конфигурации MvkModbus не задана.` |
| Выход из диапазона | `{Путь} = {значение} вне допустимого диапазона {min}..{max}.` |
| Потолок реконнекта меньше базы | `Defaults.ReconnectMaxDelayMs не может быть меньше Defaults.ReconnectBaseDelayMs.` |
| Конец защищённой области меньше начала | `Defaults.ProtectedWriteEndAddress не может быть меньше Defaults.ProtectedWriteStartAddress.` |
| `null` в списке устройств | `В списке устройств найден пустой элемент.` |
| Пустой `Id` | `У устройства не задан обязательный идентификатор Id.` |
| Дубль `Id` | `Устройство '{id}': идентификатор Id не уникален.` |
| Пустой `Host` | `Устройство '{id}': не задан адрес Host.` |

Ошибки собираются списком — возвращаются **все** найденные проблемы, а не первая.
Дубликаты определяются без учёта регистра и после `Trim`: `MVK-1` и `mvk-1` —
дубликат. Если у устройства пустой `Id`, проверка его `Host` не выполняется —
в списке будет только ошибка про `Id`.

### Пример вывода

Конфигурация с тремя ошибками через DI (получено при обращении к настройкам):

```
Microsoft.Extensions.Options.OptionsValidationException:
Устройство 'a': не задан адрес Host.; Устройство 'a': идентификатор Id не уникален.;
Устройство 'a'.Port = 70000 вне допустимого диапазона 1..65535.
```

Полный список доступен в свойстве `Failures`. При старте хоста исключение
приходит из `host.StartAsync()` (лог «Hosting failed to start»).

Автономный загрузчик упаковывает тот же список в одно исключение:

```
MvkModbusException: Некорректная конфигурация 'MvkModbus':
Defaults.ReconnectMaxDelayMs не может быть меньше Defaults.ReconnectBaseDelayMs.
Defaults.ProtectedWriteEndAddress не может быть меньше Defaults.ProtectedWriteStartAddress.
Устройство 'a': не задан адрес Host. Устройство 'A': идентификатор Id не уникален.
У устройства не задан обязательный идентификатор Id.
```

(Свойство `ErrorKind` равно `MvkErrorKind.Configuration`.)

### Ошибки уровня биндера

Если значение не преобразуется к типу свойства, до валидатора дело не доходит —
стандартный биндер бросает `InvalidOperationException`:

| Пример | Исключение |
|---|---|
| `"ByteOrder": "BigEndiann"` | `Failed to convert configuration value at 'MvkModbus:Defaults:ByteOrder' to type 'Mvk.Modbus.Configuration.MvkByteOrder'.` |
| `"SlaveId": 300` (не влезает в `byte`) | `Failed to convert ... to type 'System.Byte'.` |
| `"Port": "abc"` | `Failed to convert ... to type 'System.Int32'.` |

### Чего валидатор не проверяет

- формат и доступность `Host` (только непустоту);
- имена middleware в `Pipeline` — их проверяет `MvkMiddlewareResolver` при
  создании пула (ошибка `MvkModbusException`, `Configuration`);
- числовые поля `Pool` (`ReconnectEnabled`, `HealthCheckIntervalSeconds`);
- значения `Tags`;
- корректность `ByteOrder` относительно фактического регистра `0x000C`.

## 10. MvkModbusOptionsLoader

Автономный загрузчик (`Mvk.Modbus.Configuration.MvkModbusOptionsLoader`) нужен,
когда DI-контейнера нет: консольные утилиты, тесты, разовые скрипты.

| Член | Сигнатура | Назначение |
|---|---|---|
| `DefaultSectionName` | `const string = "MvkModbus"` | Имя секции по умолчанию |
| `BuildConfiguration` | `static IConfigurationRoot BuildConfiguration(string jsonFilePath)` | Создать конфигурацию из JSON-файла |
| `LoadFromJsonFile` | `static MvkModbusOptions LoadFromJsonFile(string jsonFilePath, string sectionName = DefaultSectionName)` | Загрузить, привязать и проверить секцию |
| `LoadFromConfiguration` | `static MvkModbusOptions LoadFromConfiguration(IConfiguration configuration, string sectionName = DefaultSectionName)` | То же из готовой конфигурации |

`BuildConfiguration` разворачивает путь (`Path.GetFullPath`), проверяет наличие
файла и подключает его через `AddJsonFile(optional: false, reloadOnChange: false)`.

### Из файла

```csharp
using Mvk.Modbus.Configuration;

MvkModbusOptions options = MvkModbusOptionsLoader.LoadFromJsonFile("mvkmodbus.json");

// Своя секция:
MvkModbusOptions custom = MvkModbusOptionsLoader.LoadFromJsonFile("mvkmodbus.json", "MvkModbusTest");
```

### Из готовой IConfiguration

```csharp
using Microsoft.Extensions.Configuration;

IConfiguration configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["MvkModbus:Defaults:RequireEvenAddressAndQuantity"] = "true",
        ["MvkModbus:Devices:0:Id"] = "mvk-1",
        ["MvkModbus:Devices:0:Host"] = "10.20.0.211"
    })
    .Build();

MvkModbusOptions options = MvkModbusOptionsLoader.LoadFromConfiguration(configuration);
```

Приложение, у которого уже есть построитель конфигурации (appsettings + окружение),
может переиспользовать его напрямую:

```csharp
IConfigurationRoot configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

MvkModbusOptions options = MvkModbusOptionsLoader.LoadFromConfiguration(configuration);
```

### Исключения

| Условие | Исключение |
|---|---|
| Пустой/`null` путь (`BuildConfiguration`) | `ArgumentException` |
| Файл не найден | `MvkModbusException` (`Configuration`) |
| Пустое имя секции | `ArgumentException` |
| `configuration == null` | `ArgumentNullException` |
| Секция отсутствует или пуста (`{}`) | `MvkModbusException` (`Configuration`): «Секция 'MvkModbus' не найдена в конфигурации.» |
| Ошибки валидации | `MvkModbusException` (`Configuration`): «Некорректная конфигурация 'MvkModbus': …» |

Обработка типовая:

```csharp
try
{
    MvkModbusOptions options = MvkModbusOptionsLoader.LoadFromJsonFile(path);
    await using MvkDevicePool pool = new(options);
}
catch (MvkModbusException ex) when (ex.ErrorKind == MvkErrorKind.Configuration)
{
    Console.Error.WriteLine($"Ошибка конфигурации: {ex.Message}");
    return;
}
```

Загрузчик ничего не логирует и не создаёт пул — это только чтение и проверка
секции. Пул из файла создаётся либо вручную (`new MvkDevicePool(options)`), либо
`MvkDevicePool.FromJsonFile(path, loggerFactory)`.

## 11. Полные примеры конфигов

### Минимальный

Достаточно `Id` и `Host` — остальное берётся из значений по умолчанию
(порт 502, SlaveId 1, BigEndian, таймауты, реконнект):

```json
{
  "MvkModbus": {
    "Devices": [
      { "Id": "mvk-1", "Host": "10.20.0.211" }
    ]
  }
}
```

### Рекомендуемый для МВК01

Явные параметры, важные для реального устройства: чётность адреса/количества,
увеличенный таймаут чтения, защита конфигурации:

```json
{
  "MvkModbus": {
    "Defaults": {
      "Port": 502,
      "SlaveId": 1,
      "ByteOrder": "BigEndian",
      "ConnectTimeoutMs": 3000,
      "ReadTimeoutMs": 2000,
      "WriteTimeoutMs": 3000,
      "MaxRetries": 2,
      "RetryDelayMs": 150,
      "ReconnectBaseDelayMs": 500,
      "ReconnectMaxDelayMs": 30000,
      "IdleDisconnectSeconds": 120,
      "MaxConcurrentRequests": 1,
      "WritePolicy": "ProtectConfigArea",
      "AllowConfigurationWrite": true,
      "ProtectedWriteStartAddress": "0x0000",
      "ProtectedWriteEndAddress": "0x0FFF",
      "RequireEvenAddressAndQuantity": true
    },
    "Pool": {
      "ReconnectEnabled": true,
      "HealthCheckIntervalSeconds": 15
    },
    "Devices": [
      {
        "Id": "mvk-1",
        "Name": "МВК01 стенд",
        "Host": "10.20.0.211",
        "Tags": { "asset": "Стенд", "location": "Лаборатория" }
      }
    ]
  }
}
```

`AllowConfigurationWrite: true` нужен только там, где приложение действительно
пишет конфигурацию (каналы, коэффициенты, системные команды Save/Reboot).
Для мониторинга «только чтение» оставьте `false`. При использовании DI
`AddMvk01Devices()` всё равно выставит `true` в `Defaults` — точечно запретить
запись можно на устройстве (`"AllowConfigurationWrite": false`).

### Несколько устройств (3 МВК + ВДМ82)

```json
{
  "MvkModbus": {
    "Defaults": {
      "Port": 502,
      "SlaveId": 1,
      "ByteOrder": "BigEndian",
      "ReadTimeoutMs": 2000,
      "RequireEvenAddressAndQuantity": true,
      "WritePolicy": "ProtectConfigArea",
      "AllowConfigurationWrite": false
    },
    "Pool": { "ReconnectEnabled": true, "HealthCheckIntervalSeconds": 15 },
    "Devices": [
      { "Id": "mvk-1", "Name": "МВК01 №1", "Host": "10.20.0.211", "Tags": { "asset": "Агрегат 1" } },
      { "Id": "mvk-2", "Name": "МВК01 №2", "Host": "10.20.0.212", "Tags": { "asset": "Агрегат 2" } },
      { "Id": "mvk-3", "Name": "МВК01 №3", "Host": "10.20.0.213", "Tags": { "asset": "Агрегат 3" } },
      {
        "Id": "vdm-82",
        "Name": "ВДМ82",
        "Host": "10.20.0.220",
        "Port": 10502,
        "SlaveId": 3,
        "ByteOrder": "Mixed",
        "Tags": { "asset": "Агрегат 3", "sensor": "вибропреобразователь" }
      }
    ]
  }
}
```

Адреса условные — замените на фактические. Поле `RequireEvenAddressAndQuantity`
задаётся только в `Defaults` и действует на весь пул: пока в пуле одни МВК01,
`true` безопасен (типизированный слой и так формирует запросы с чётными
адресами), но при добавлении устройства, которому нужны нечётные адреса,
придётся оставить `false` или вынести такое устройство в отдельный пул
с собственной конфигурацией.

### Отключённое устройство

```json
{
  "MvkModbus": {
    "Devices": [
      { "Id": "mvk-1", "Host": "10.20.0.211" },
      { "Id": "mvk-2", "Host": "10.20.0.212", "Enabled": false }
    ]
  }
}
```

Устройство остаётся в пуле (видно в `pool.Devices`), но любой запрос к нему
сразу возвращает ошибку `MvkErrorKind.DeviceDisabled`; фоновый реконнект его
не трогает. Удобно для временно выведенного из работы оборудования.

### Конфиг «только чтение»

```json
{
  "MvkModbus": {
    "Defaults": {
      "WritePolicy": "ReadOnly",
      "RequireEvenAddressAndQuantity": true
    },
    "Devices": [
      { "Id": "mvk-1", "Host": "10.20.0.211" }
    ]
  }
}
```

Любая попытка записи через пайплайн завершается `Validation`-ошибкой.
Поле `AllowConfigurationWrite` в этом режиме ни на что не влияет.

## 12. Практические советы

### Таймауты для медленных каналов

По умолчанию `ReadTimeoutMs = 1000`. Если сеть загружена, устройство отвечает
с задержкой или выполняются «тяжёлые» операции, увеличьте `ReadTimeoutMs`
до 2000–5000 мс. Системные команды МВК01 (Save) могут отвечать до ~3 с —
библиотека сама задаёт им отдельный лимит, но `WriteTimeoutMs` (3000)
лучше не уменьшать. `ConnectTimeoutMs` увеличивайте при работе через
нестабильные каналы (GPRS, VPN); диапазон позволяет до 600000 мс.

### Когда увеличивать MaxRetries

Ретраи спасают при единичных сбоях: потерянных пакетах, кратковременных
разрывах. Но каждая повторная попытка — это `RetryDelayMs × N` ожидания плюс
возможный повторный коннект, поэтому при устойчивом обрыве рост `MaxRetries`
только замедляет выдачу ошибки. Разумные значения — 2–3; `0` — если приложение
само управляет повторами. Напоминание: повторяются только временные ошибки
(`Transport`, `Timeout`, `Busy`), ошибки Modbus-протокола (`DeviceError`) и
`Validation` не повторяются.

### Когда IdleDisconnectSeconds = 0

`0` отключает закрытие простаивающего соединения. Это оправдано, если:

- устройство опрашивается часто (интервал меньше `IdleDisconnectSeconds`),
  и вы не хотите тратить время на повторный TCP-handshake;
- канал медленный или с высоким RTT;
- на устройстве ограничено число одновременных TCP-соединений, и важно, чтобы
  ваш клиент держал одно.

Плата — постоянно открытые сокеты. При редком опросе (раз в минуты) выгоднее
оставить 120 с.

### Почему MaxConcurrentRequests обычно 1

Modbus/TCP — последовательный протокол: устройство обрабатывает запросы
по одному, а один экземпляр транспорта NModbus не рассчитан на параллельные
вызовы. Значение по умолчанию 1 сериализует обращения к устройству и делает
поведение таймаутов предсказуемым. Увеличивать лимит стоит только при
уверенности, что транспорт и устройство выдержат конкуренцию; для МВК01
рекомендуемое значение — 1.

### Изоляция конфигурации по средам

Стандартные приёмы .NET работают без доработок библиотеки:

- **appsettings + окружение.** Хост сам подключает `appsettings.json` и
  `appsettings.{Environment}.json`; секцию `MvkModbus` удобно держать в них.
  Для отладочных стендов заведите `appsettings.Development.json` с другим
  `Host`/портом и таймаутами.
- **Переменные окружения.** Двойное подчёркивание разделяет уровни:
  `MvkModbus__Devices__0__Host=10.20.0.211`,
  `MvkModbus__Defaults__ReadTimeoutMs=5000`.
- **Optional-файлы.** `AddJsonFile("mvkmodbus.local.json", optional: true)` —
  локальные переопределения не попадают в репозиторий; провайдер, добавленный
  позже, имеет приоритет.
- **Отдельное имя секции** для второго пула в одном процессе:
  `AddMvkModbus(configuration, "MvkModbusTest")`.

Полезно знать: JSON-провайдер .NET допускает комментарии и запятые в конце
блоков, имена ключей и значения перечислений нечувствительны к регистру
(`"byteorder": "mixed"` сработает). Это удобно, но злоупотреблять не стоит —
пишите канонические имена.

## 13. Что будет, если

### 13.1. Опечатка в имени поля

Неизвестные ключи JSON стандартный биндер молча игнорирует. Опечатка не
приводит ни к ошибке, ни к предупреждению — просто применяется значение
по умолчанию:

```json
{
  "MvkModbus": {
    "Defaults": { "ReadTimaoutMs": 5000 },
    "Devices": [ { "Id": "mvk-1", "Host": "10.20.0.211" } ]
  }
}
```

Здесь опечатка `ReadTimaoutMs` останется незамеченной, действующий таймаут —
1000 мс. По той же причине `"Default"` (вместо `Defaults`) тихо отбросит весь
блок настроек. Опечатка в *значении* перечисления — другой случай:
`"ByteOrder": "BigEndiann"` вызовет `InvalidOperationException` от биндера
(раздел 9).

### 13.2. Pipeline пустой

Два разных случая:

- **Программно** `options.Pipeline = []` — `ResolvePipeline()` вернёт пустой
  список, и пул будет работать без middleware: без проверки чётности, ретраев,
  метрик, логирования и защиты записи. Ответственность за корректность
  запросов ложится на вызывающий код.
- **В JSON** `"Pipeline": []` — биндер оставляет `Pipeline = null`
  (пустой массив не создаёт ключей), и применяется порядок по умолчанию.
  То же произойдёт, если ключ просто отсутствует.

Неизвестное имя в непустом `Pipeline` (например, `"nope"`) — ошибка:
при создании пула в DI `MvkMiddlewareResolver` бросит `MvkModbusException`
(`Configuration`) с текстом «Middleware 'nope' не найден…». Автономный пул
(`new MvkDevicePool(options)`, `MvkDevicePool.FromJsonFile`) поле `Pipeline`
не использует вовсе и всегда работает с набором middleware по умолчанию.

### 13.3. Devices пустой

Отсутствие ключа `Devices` или пустой массив `[]` дают `Devices = null` →
`ResolveDevices()` вернёт пустой список. Это не ошибка: пул создаётся пустым,
`pool.Devices.Count == 0`. Любой `pool.GetDevice("...")` бросит
`MvkModbusException` с `MvkErrorKind.DeviceNotFound`; устройства можно добавить
на лету через `pool.AddDevice(new MvkDeviceOptions { ... })`.

### 13.4. Дубликаты

Дубликат `Id` (без учёта регистра, после `Trim`) — ошибка валидации:

- в DI — `OptionsValidationException` при старте хоста или первом обращении
  к настройкам;
- в автономном загрузчике — `MvkModbusException` (`Configuration`).

При динамическом добавлении `pool.AddDevice(...)` дубликат даёт
`MvkModbusException` (`Configuration`): «Устройство '...' уже существует
в пуле.»

### 13.5. Порт занят / соединение не устанавливается

Клиентский сокет приложение не слушает, поэтому «занят» — это про сторону
устройства или промежуточную инфраструктуру: закрытый порт, firewall, лимит
одновременных TCP-подключений, занятый другим клиентом порт на устройстве.
В любом случае:

- создание пула **не падает** — соединения устанавливаются лениво, при первом
  запросе;
- запрос вернёт ошибку `Timeout` (истёк `ConnectTimeoutMs`) или `Transport`
  (сокетная ошибка);
- устройство перейдёт в состояние `Faulted`, и при `ReconnectEnabled = true`
  начнётся фоновый реконнект с нарастающей задержкой.

Диагностика: `Test-NetConnection <host> -Port <port>` и лог библиотеки
(«Не удалось подключиться…», «Таймаут подключения…»).

### 13.6. Устройство недоступно при старте

Пул стартует штатно: при конструировании `MvkDevicePool` ни один сокет
не открывается. Состояние устройства остаётся `Disconnected`, первый запрос
пытается подключиться лениво. Если устройство так и не ответило:

1. запрос завершается ошибкой (`Timeout`/`Transport`), устройство переходит
   в `Faulted`, срабатывает событие `DeviceStateChanged`;
2. фоновый цикл (раз в `HealthCheckIntervalSeconds`) пытается переподключиться
   с задержкой `min(ReconnectBaseDelayMs × 2^(N−1), ReconnectMaxDelayMs)`;
3. после успешного запроса счётчик неудач сбрасывается, устройство снова
   `Connected`.

Исключение — явный `EnsureConnectedAsync` (и `Mvk01Connection.ConnectAsync`):
он проверяет связь сразу и бросает `MvkModbusException`, если устройство
недоступно.

## Подводные камни

- **Пустой массив в JSON — это не пустой список.** `"Pipeline": []` и
  `"Devices": []` биндер оставляет как `null`: `Pipeline` превращается
  в порядок по умолчанию, `Devices` — в пустой пул. Пустой пайплайн
  без middleware задаётся только программно (`options.Pipeline = []`).
- **Pipeline учитывается только в DI.** `MvkDevicePool`/`FromJsonFile` всегда
  используют набор middleware по умолчанию; имена из секции `Pipeline` там
  не применяются и не проверяются.
- **AddMvk01Devices переопределяет Defaults.** `PostConfigure` принудительно
  выставляет `RequireEvenAddressAndQuantity = true` и
  `AllowConfigurationWrite = true`, перезаписывая значения из файла
  (сузить можно на уровне устройства).
- **Поля устройства — только пять.** Таймауты, ретраи, `WritePolicy`,
  защищённая область и `IdleDisconnectSeconds` переопределить у отдельного
  устройства нельзя — они общие для пула.
- **`SlaveId` — тип `byte`.** Значение больше 255 падает ещё на биндере
  (`InvalidOperationException`), а не на валидаторе.
- **Опечатки в именах полей не диагностируются.** Неизвестные ключи молча
  игнорируются — проверяйте конфиг по этой главе; неверное значение
  перечисления, наоборот, приводит к исключению биндера.
- **Горячего применения конфигурации нет.** `IOptions` и пул фиксируют
  настройки при старте; `reloadOnChange: true` не пересоздаёт устройства.
- **Валидация зависит от способа подключения.** Через `AddMvkModbus` это
  `OptionsValidationException` (`ValidateOnStart` / `IOptions.Value`), через
  `MvkModbusOptionsLoader` — `MvkModbusException` (`Configuration`).
- **`MvkDeviceSettings.Tags` всегда не `null`**, но при отсутствии меток пуст;
  поиск по ключу регистронезависим.
- **Защита записи работает только при `writeProtection` в пайплайне.** Убрав
  middleware, вы отключаете и политику `WritePolicy`.
- **На реальном устройстве без явного разрешения — только чтение.**
  `AllowConfigurationWrite: true` в примерах главы означает готовность
  приложения к записи, а не разрешение запускать запись на стенде.

## См. также

- [Пул устройств](05-device-pool.md)
- [Транспорт](04-transport-and-raw-registers.md)
- [README](../README.md)
- [Глава 02. Первое подключение](02-getting-started.md)
- [Пайплайн middleware](06-pipeline-middleware.md)
- [PLAN.md](../PLAN.md)
