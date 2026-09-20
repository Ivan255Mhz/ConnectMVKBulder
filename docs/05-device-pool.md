# 05. Пул устройств

Пул `MvkDevicePool` — центральная сущность библиотеки для работы с несколькими МВК.
Он хранит настройки всех устройств, создаёт по объекту `MvkDevice` на каждый
идентификатор, сериализует запросы, отслеживает состояние соединений и в фоне
восстанавливает потерянную связь.

В этой главе разобраны:

- зачем нужен пул и что он даёт по сравнению с одиночным подключением;
- интерфейс `IMvkDevicePool` и его реализация `MvkDevicePool`;
- интерфейс `IMvkDevice` и контракты ошибок;
- состояния устройства и событие `MvkDeviceStateChangedEventArgs`;
- жизненный цикл соединения: ленивое подключение, авто-разрыв, реконнект с backoff,
  отключение простаивающих соединений;
- сериализация запросов и настройка `MaxConcurrentRequests`;
- управление составом пула на лету;
- практические примеры, производительность и ограничения.

Все примеры предполагают устройство с `Id = "mvk-1"`; фрагменты конфигурации
соответствуют секции `MvkModbus` (см. [Конфигурация](03-configuration.md)).

---

## 1. Зачем нужен пул

Для одного МВК есть простая точка входа — `Mvk01Connection.ConnectAsync(host)`
(см. [README](../README.md), раздел «Быстрый старт»). Она создаёт транспорт
на одно устройство и подходит для одиночных сценариев. Как только устройств
становится больше одного, появляются задачи, которые `Mvk01Connection`
не решает: единая конфигурация, восстановление связи, учёт ресурсов, события.

| Аспект | Одно устройство (`Mvk01Connection`) | Пул (`MvkDevicePool`) |
|---|---|---|
| Конфигурация | Параметры задаются кодом | Единая секция `MvkModbus`: `Defaults` + список `Devices` |
| Подключение | Явное, сразу при создании | Ленивое: сокет открывается при первом запросе |
| Разрыв связи | Ошибка возвращается вызывающему коду | Авто-разрыв при ошибке, реконнект с экспоненциальным backoff |
| Простой | Соединение живёт, пока живёт объект | Idle-отключение освобождает сокеты неактивных устройств |
| События | Нет | `StateChanged` у устройства и `DeviceStateChanged` у пула |
| Состав | Фиксирован | `AddDevice`/`RemoveDevice` на лету |
| Повторное использование | Новый объект на каждое подключение | Один объект устройства на `Id`, общий для всех потребителей |
| Интеграция | Ручное создание/освобождение | DI: `AddMvkModbus` регистрирует `IMvkDevicePool` как singleton |

Что именно даёт пул:

- **ленивое подключение** — при создании пула сокеты не открываются; соединение
  устанавливается при первом запросе к устройству;
- **автоматический реконнект** — фоновый цикл обслуживания (или ручной вызов
  `MaintainAsync`) восстанавливает соединение после временных ошибок;
- **idle-отключение** — неактивные соединения закрываются по `IdleDisconnectSeconds`,
  что экономит сокеты и файловые дескрипторы;
- **события состояния** — единая точка подписки на переходы всех устройств
  (`Disconnected`, `Connecting`, `Connected`, `Faulted`);
- **централизованный конфиг** — настройки по умолчанию задаются один раз и
  переопределяются только там, где это нужно;
- **DI** — потребители получают `IMvkDevicePool` из контейнера; один экземпляр пула
  и одни и те же объекты устройств используются всем приложением;
- **сериализация запросов** — к одному устройству запросы выполняются по очереди,
  вызывающий код не обязан синхронизироваться сам.

---

## 2. `IMvkDevicePool`

```csharp
public interface IMvkDevicePool : IAsyncDisposable
{
    MvkModbusOptions Options { get; }

    IReadOnlyCollection<IMvkDevice> Devices { get; }

    event EventHandler<MvkDeviceStateChangedEventArgs>? DeviceStateChanged;

    IMvkDevice GetDevice(string id);

    bool TryGetDevice(string id, out IMvkDevice device);

    IMvkDevice AddDevice(MvkDeviceOptions deviceOptions);

    bool RemoveDevice(string id);

    Task MaintainAsync(CancellationToken cancellationToken = default);
}
```

### Члены интерфейса

| Член | Возвращает | Поведение и ошибки |
|---|---|---|
| `Options` | `MvkModbusOptions` | Корневая конфигурация: `Defaults`, `Pool`, `Pipeline`, `Devices`. Ссылка на тот же объект, что передан в конструктор |
| `Devices` | `IReadOnlyCollection<IMvkDevice>` | Снимок всех устройств пула (включая отключённые). Каждое обращение создаёт новый список |
| `DeviceStateChanged` | событие | Пересылает `StateChanged` любого устройства пула |
| `GetDevice(id)` | `IMvkDevice` | Поиск по `Id` без учёта регистра; не найдено — `MvkModbusException` (`DeviceNotFound`); пустой `id` — `ArgumentException` |
| `TryGetDevice(id, out device)` | `bool` | То же без исключения для ненайденного `Id`, но пустой `id` по-прежнему `ArgumentException` |
| `AddDevice(deviceOptions)` | `IMvkDevice` | Добавляет устройство на лету; дубликат `Id` (без учёта регистра) — `MvkModbusException` (`Configuration`) |
| `RemoveDevice(id)` | `bool` | Удаляет устройство, отписывает события и освобождает ресурсы; `false`, если `Id` пуст или не найден |
| `MaintainAsync(ct)` | `Task` | Один цикл обслуживания вручную: реконнект `Faulted`-устройств и отключение простаивающих |

### `Options`

```csharp
IMvkDevicePool pool = ...;

Console.WriteLine($"Устройств: {pool.Devices.Count}, " +
                  $"интервал обслуживания: {pool.Options.Pool.HealthCheckIntervalSeconds} с");

MvkDefaultsOptions defaults = pool.Options.Defaults;
Console.WriteLine($"Порт по умолчанию: {defaults.Port}, SlaveId: {defaults.SlaveId}");
```

`Options` — обычный изменяемый объект. Менять его после создания пула не имеет
смысла: параметры устройств уже развёрнуты в неизменяемые `MvkDeviceSettings`,
а интервал обслуживания читается один раз при запуске фонового цикла.
Настраивайте конфигурацию до создания пула.

### `Devices`

```csharp
foreach (IMvkDevice device in pool.Devices)
{
    Console.WriteLine(
        $"{device.Settings.Id,-8} {device.Settings.Endpoint,-18} " +
        $"{device.State,-12} подключено: {device.IsConnected}");
}
```

`Devices` возвращает снимок — вызывать его в горячем цикле дорого
(каждое обращение копирует коллекцию). Если состав пула не меняется, сохраните
результат в локальную переменную.

### `DeviceStateChanged`

```csharp
pool.DeviceStateChanged += (_, args) =>
{
    Console.WriteLine(
        $"[{DateTimeOffset.Now:HH:mm:ss}] {args.Device.Settings.Id}: " +
        $"{args.OldState} -> {args.NewState}" +
        (args.Error is null ? string.Empty : $" | {args.Error.ErrorKind}: {args.Error.Message}"));
};
```

Обработчик вызывается синхронно из того потока, который изменил состояние:
потока запроса или потока фонового обслуживания. Быстрые операции в обработчике —
норма, тяжёлая работа и блокировки — риск (см. «Подводные камни»).

### `GetDevice`

```csharp
IMvkDevice device = pool.GetDevice("mvk-1");

ushort[] registers = await device.ReadHoldingRegistersAsync(0x1004, 2);
```

Идентификатор чувствителен к регистру только формально: сравнение выполняется
через `StringComparer.OrdinalIgnoreCase`, поэтому `pool.GetDevice("MVK-1")`
вернёт то же устройство, что и `pool.GetDevice("mvk-1")`.

Если устройства нет:

```csharp
try
{
    IMvkDevice missing = pool.GetDevice("mvk-404");
}
catch (MvkModbusException ex) when (ex.ErrorKind == MvkErrorKind.DeviceNotFound)
{
    Console.WriteLine($"Нет такого устройства: {ex.DeviceId}");
}
```

### `TryGetDevice`

```csharp
if (pool.TryGetDevice("mvk-1", out IMvkDevice device))
{
    ushort[] data = await device.ReadHoldingRegistersAsync(0x1004, 2);
}
else
{
    Console.WriteLine("Устройство 'mvk-1' отсутствует в пуле");
}
```

Для сценариев «пользователь ввёл идентификатор» предпочитайте `TryGetDevice`:
он не бросает исключение на ненайденный `Id`. Пустая строка или пробелы —
по-прежнему `ArgumentException`, проверяйте входные данные.

### `AddDevice`

```csharp
IMvkDevice added = pool.AddDevice(new MvkDeviceOptions
{
    Id = "mvk-3",
    Name = "Резервный стенд",
    Host = "10.20.0.213"
});

await added.EnsureConnectedAsync();   // подключение не выполняется автоматически
```

Дубликат идентификатора:

```csharp
try
{
    pool.AddDevice(new MvkDeviceOptions { Id = "MVK-3", Host = "10.20.0.214" });
}
catch (MvkModbusException ex) when (ex.ErrorKind == MvkErrorKind.Configuration)
{
    Console.WriteLine($"Не добавлено: {ex.Message}");   // Id уже занят (регистр не важен)
}
```

### `RemoveDevice`

```csharp
if (pool.RemoveDevice("mvk-3"))
{
    Console.WriteLine("Устройство удалено из пула");
}

// Повторное удаление — false, без исключения
bool removedAgain = pool.RemoveDevice("mvk-3");
```

`RemoveDevice` перед удалением отписывает событие и вызывает `Dispose()` устройства:
сокет закрывается, ожидающие запросы обречены (см. «Подводные камни»).

### `MaintainAsync`

```csharp
await pool.MaintainAsync(cancellationToken);
```

Метод выполняет один проход обслуживания: для каждого устройства проверяет
необходимость реконнекта (`Faulted` + истёк backoff) и закрывает простаивающие
соединения. В обычной работе его вызывает фоновый цикл, но ручной вызов полезен:

- в тестах и на стендах без фонового цикла (`ReconnectEnabled = false`);
- в приложениях со своим планировщиком (например, обслуживание по внешнему таймеру).

---

## 3. `MvkDevicePool`

`MvkDevicePool` — `sealed`-реализация `IMvkDevicePool`, одновременно
`IAsyncDisposable` и `IDisposable`.

### Конструктор

```csharp
public MvkDevicePool(
    MvkModbusOptions options,
    IMvkTransportFactory? transportFactory = null,
    IEnumerable<IModbusMiddleware>? middlewares = null,
    ILoggerFactory? loggerFactory = null)
```

| Параметр | Обязательный | По умолчанию | Смысл |
|---|---|---|---|
| `options` | да | — | Корневая конфигурация `MvkModbusOptions`; из неё берутся `Defaults`, `Devices` и `Pool` |
| `transportFactory` | нет | `new NModbusTransportFactory(loggerFactory)` | Фабрика транспортов: создаёт по одному `IMvkTransport` на устройство. Подменяется для тестов и нестандартных транспортов (COM-порт, прокси) |
| `middlewares` | нет | `MvkPipelineFactory.CreateDefaultMiddlewares(loggerFactory)` | Набор middleware в порядке «первый — самый внешний». Пустая коллекция — все запросы идут напрямую в транспорт |
| `loggerFactory` | нет | `NullLoggerFactory.Instance` | Фабрика логгеров для пула, устройств, транспортов и встроенных middleware |

Что делает конструктор:

1. сохраняет `options` и создаёт логгер `ILogger<MvkDevicePool>`;
2. создаёт устройства по списку `options.ResolveDevices()` — для каждого
   вызывается `MvkDeviceOptions.Resolve(Defaults)`, результат фиксируется
   в неизменяемом `MvkDeviceSettings`;
3. для каждого устройства создаёт транспорт через фабрику и собирает
   `MvkPipeline` с терминалом `MvkPipeline.TerminalFromTransport(transport)`;
4. если `options.Pool.ReconnectEnabled == true`, запускает фоновый цикл
   обслуживания (`Task.Run`), который живёт до освобождения пула.

Сокеты при создании не открываются: подключение ленивое.

### `FromJsonFile`

```csharp
await using MvkDevicePool pool = MvkDevicePool.FromJsonFile("mvkmodbus.json");
```

Второй (необязательный) параметр — `ILoggerFactory`; без него пул, устройства
и транспорты используют no-op логгеры. Статический метод читает и валидирует секцию `MvkModbus`
(через `MvkModbusOptionsLoader.LoadFromJsonFile`) и возвращает готовый пул.
Загрузка файла синхронная; при отсутствии файла или некорректной конфигурации
выбрасывается `MvkModbusException` с `MvkErrorKind.Configuration`.

### Фоновый цикл обслуживания

```csharp
private async Task MaintenanceLoopAsync(CancellationToken cancellationToken)
{
    int intervalSeconds = Math.Max(1, Options.Pool.HealthCheckIntervalSeconds);

    using PeriodicTimer timer = new(TimeSpan.FromSeconds(intervalSeconds));
    ...
}
```

- цикл запускается только при `Pool.ReconnectEnabled = true` (значение по умолчанию);
- период — `Pool.HealthCheckIntervalSeconds` (по умолчанию 15 с); значение меньше 1
  приводится к 1 с;
- каждый тик вызывается `MaintainAsync`, ошибки тика логируются предупреждением
  и не останавливают цикл;
- интервал читается один раз при входе в цикл: изменить
  `HealthCheckIntervalSeconds` после создания пула нельзя;
- при `ReconnectEnabled = false` фонового цикла нет: реконнект и idle-отключение
  выполняются только при ручном вызове `MaintainAsync`.

### Освобождение: `IAsyncDisposable` и `IDisposable`

```csharp
await using MvkDevicePool pool = new(options);
// ... работа ...

// либо явно
await pool.DisposeAsync();
```

`DisposeAsync`:

1. отменяет токен фонового цикла и дожидается его завершения;
2. отписывает `StateChanged` у каждого устройства и вызывает `Dispose()`;
3. очищает словарь устройств.

Синхронный `Dispose()` — обёртка над `DisposeAsync` для контейнеров, умеющих
освобождать службы только синхронно; в остальных случаях используйте
`await using`/`DisposeAsync`.

### Потокобезопасность

| Механизм | Что защищает |
|---|---|
| `ConcurrentDictionary<string, MvkDevice>` (`OrdinalIgnoreCase`) | Состав пула: параллельные `AddDevice`/`RemoveDevice`/`GetDevice`, регистронезависимый поиск |
| `lock (stateLock)` в `MvkDevice` | Поля состояния: `State`, `LastError`, `nextReconnectAt`, счётчик ошибок |
| `Interlocked` | `LastRequestAt` и время последней активности |
| `SemaphoreSlim` в `MvkDevice` | Сериализация запросов к одному устройству |
| `SemaphoreSlim` в транспорте NModbus | Параллельные `ConnectAsync` (например, `EnsureConnectedAsync` из нескольких потоков) |

События `StateChanged`/`DeviceStateChanged` вызываются вне блокировки состояния,
поэтому обработчик может вызвать методы устройства без риска взаимоблокировки.
При этом сам обработчик выполняется в потоке инициатора перехода — учитывайте
это при работе с UI.

---

## 4. `IMvkDevice`

```csharp
public interface IMvkDevice : IDisposable
{
    MvkDeviceSettings Settings { get; }
    MvkDeviceState State { get; }
    bool IsConnected { get; }
    MvkModbusException? LastError { get; }
    DateTimeOffset LastRequestAt { get; }

    event EventHandler<MvkDeviceStateChangedEventArgs>? StateChanged;

    Task<ModbusResponse> ExecuteAsync(ModbusRequest request, CancellationToken cancellationToken = default);
    Task<ModbusResponse> ExecuteAsync(ModbusRequest request, TimeSpan timeout, CancellationToken cancellationToken = default);
    Task EnsureConnectedAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task<ushort[]> ReadHoldingRegistersAsync(ushort address, ushort quantity, CancellationToken cancellationToken = default);
    Task<ushort[]> ReadInputRegistersAsync(ushort address, ushort quantity, CancellationToken cancellationToken = default);
    Task WriteSingleRegisterAsync(ushort address, ushort value, CancellationToken cancellationToken = default);
    Task WriteMultipleRegistersAsync(ushort address, ushort[] data, CancellationToken cancellationToken = default);
}
```

### Члены интерфейса

| Член | Тип | Описание |
|---|---|---|
| `Settings` | `MvkDeviceSettings` | Неизменяемые параметры с применёнными `Defaults`: `Id`, `Host`, `Port`, `SlaveId`, `ByteOrder`, таймауты, `Endpoint`, `Tags` и др. |
| `State` | `MvkDeviceState` | Текущее состояние (`Disconnected`/`Connecting`/`Connected`/`Faulted`) |
| `IsConnected` | `bool` | Фактическое состояние сокета транспорта; может отличаться от `State` (сокет живёт, а `Faulted` уже выставлен — и наоборот) |
| `LastError` | `MvkModbusException?` | Последняя ошибка обращения; сбрасывается в `null` при успехе |
| `LastRequestAt` | `DateTimeOffset` | Время последнего запроса (UTC), `DateTimeOffset.MinValue` до первого запроса |
| `StateChanged` | событие | Переход состояния устройства |
| `ExecuteAsync(request, ct)` | `Task<ModbusResponse>` | Запрос через пайплайн; ошибки возвращаются в `ModbusResponse.Error` |
| `ExecuteAsync(request, timeout, ct)` | `Task<ModbusResponse>` | То же с общим таймаутом операции (через `TimeoutMiddleware`) |
| `EnsureConnectedAsync(ct)` | `Task` | Явно установить соединение; отключённое устройство — `DeviceDisabled`, недоступность — `MvkModbusException` |
| `DisconnectAsync(ct)` | `Task` | Закрыть соединение и перевести состояние в `Disconnected` |
| `ReadHoldingRegistersAsync(addr, qty, ct)` | `Task<ushort[]>` | Чтение регистров хранения (0x03) |
| `ReadInputRegistersAsync(addr, qty, ct)` | `Task<ushort[]>` | Чтение входных регистров (0x04) |
| `WriteSingleRegisterAsync(addr, value, ct)` | `Task` | Запись одного регистра (0x06) |
| `WriteMultipleRegistersAsync(addr, data, ct)` | `Task` | Запись нескольких регистров (0x10) |

Объекты `IMvkDevice` создаёт и освобождает пул. Прикладной код не вызывает
`Dispose()` у устройства: удаление — только через `pool.RemoveDevice(id)`
или освобождение пула целиком.

### Контракты ошибок

| Метод | Временная ошибка | Нетранзиентная ошибка |
|---|---|---|
| `ExecuteAsync` | `ModbusResponse.Failure(error)` | `ModbusResponse.Failure(error)` |
| Обёртки чтения/записи | `throw MvkModbusException` | `throw MvkModbusException` |
| `EnsureConnectedAsync` | `throw MvkModbusException` (`Transport`/`Timeout`) | `throw MvkModbusException` (`DeviceDisabled` и др.) |
| `DisconnectAsync` | — | — |

Дополнительно:

- отмена вызывающим кодом (`CancellationToken`) — всегда `OperationCanceledException`,
  а не `MvkModbusException`; ожидание в очереди к устройству тоже прерывается токеном;
- вызов метода у освобождённого устройства — `ObjectDisposedException`;
- у отключённого в конфигурации устройства (`Enabled = false`) `ExecuteAsync`
  возвращает ошибку `DeviceDisabled`, не открывая сокет.

`IsTransient` у `MvkModbusException` истинно для `Transport`, `Timeout` и `Busy`
(см. [README](../README.md), раздел «Ошибки»).

### `ExecuteAsync` без исключений

```csharp
using Mvk.Modbus.Protocol;

ModbusResponse response = await device.ExecuteAsync(
    ModbusRequest.ReadHoldingRegisters("mvk-1", 0x1004, 2),
    cancellationToken);

if (response.IsSuccess)
{
    ushort[] registers = response.Data;
    Console.WriteLine($"ПО: {registers[0]:X4} {registers[1]:X4}");
}
else
{
    Console.WriteLine($"Ошибка: {response.Error!.ErrorKind} — {response.Error.Message}");
}
```

### `ExecuteAsync` с общим таймаутом

```csharp
ModbusResponse response = await device.ExecuteAsync(
    ModbusRequest.WriteMultipleRegisters("mvk-1", 0x0100, [0x1111, 0x2222]),
    TimeSpan.FromSeconds(3),
    cancellationToken);
```

Таймаут передаётся в `ModbusContext.Items[MvkContextItems.TimeoutMs]` и
обрабатывается `TimeoutMiddleware`: если операция не уложилась, возвращается
ошибка `MvkErrorKind.Timeout`. Это не замена сокетным таймаутам
(`ReadTimeoutMs`/`WriteTimeoutMs`), а ограничение сверху на всю цепочку.

### Обёртки чтения/записи

```csharp
// чтение (исключение при неуспехе)
ushort[] data = await device.ReadHoldingRegistersAsync(0x1004, 2, cancellationToken);

// запись (исключение при неуспехе)
await device.WriteSingleRegisterAsync(0x0002, 0x0001, cancellationToken);
await device.WriteMultipleRegistersAsync(0x0100, [0x1111, 0x2222], cancellationToken);
```

Обёртки строят `ModbusRequest` от имени устройства (`Settings.Id`) и возвращают
`response.Data`; при неуспехе выбрасывают `response.Error`.

### `Settings`

```csharp
MvkDeviceSettings settings = device.Settings;

Console.WriteLine(
    $"{settings.Id} [{settings.Endpoint}] SlaveId={settings.SlaveId} " +
    $"ByteOrder={settings.ByteOrder} MaxConcurrentRequests={settings.MaxConcurrentRequests}");

if (settings.Tags.TryGetValue("asset", out string? asset))
{
    Console.WriteLine($"Агрегат: {asset}");
}
```

`MvkDeviceSettings` — неизменяемая запись (`record`) со свойствами `required`:
после создания устройства её значения не меняются.

### `EnsureConnectedAsync` и `DisconnectAsync`

```csharp
try
{
    await device.EnsureConnectedAsync(cancellationToken);
    Console.WriteLine("Связь есть");
}
catch (MvkModbusException ex)
{
    Console.WriteLine($"Недоступно: {ex.ErrorKind} — {ex.Message}");
}

await device.DisconnectAsync();
```

`EnsureConnectedAsync` переводит устройство в `Connecting` → `Connected`
(или `Faulted`/`Disconnected` при ошибке). Подключение выполняется под
внутренним `SemaphoreSlim` транспорта, поэтому параллельные вызовы безопасны.

---

## 5. Состояния устройства

```csharp
public enum MvkDeviceState
{
    Disconnected = 0,
    Connecting   = 1,
    Connected    = 2,
    Faulted      = 3
}
```

### Таблица состояний

| Состояние | Значение | Когда устанавливается | Что происходит дальше |
|---|---|---|---|
| `Disconnected` | Соединение не установлено | Начальное состояние; после `DisconnectAsync`; после idle-отключения; после отмены подключения; после нетранзиентной ошибки при закрытом сокете | Следующий запрос подключится лениво; реконнект не планируется |
| `Connecting` | Выполняется подключение | Только в `EnsureConnectedAsync` (включая реконнект из `MaintainAsync`) | Успех → `Connected`; ошибка → `Faulted`; отмена → `Disconnected` |
| `Connected` | Соединение установлено, устройство отвечает | После успешного подключения и успешного запроса | Транспортная ошибка → `Faulted`; простой → `Disconnected` |
| `Faulted` | Связь потеряна, ожидается реконнект | После временной ошибки (`Transport`, `Timeout`, `Busy`) | `MaintainAsync` пробует переподключиться после истечения backoff |

### Переходы

```text
(старт пула) ──▶ Disconnected

Disconnected ──ExecuteAsync (ленивое подключение)──▶ Connected | Faulted | Disconnected
Disconnected ──EnsureConnectedAsync──▶ Connecting
Disconnected ──Enabled = false──▶ остаётся Disconnected,
                                   запросы возвращают ошибку DeviceDisabled

Connecting ──успех──▶ Connected
Connecting ──MvkModbusException──▶ Faulted
Connecting ──OperationCanceledException──▶ Disconnected

Connected ──успешный запрос──▶ Connected (LastError = null)
Connected ──DisconnectAsync / idle-отключение──▶ Disconnected
Connected ──транзиентная ошибка──▶ Faulted
Connected ──нетранзиентная ошибка──▶ Connected | Disconnected
                                      (по фактическому IsConnected транспорта)

Faulted ──MaintainAsync: переподключение успешно──▶ Connected
Faulted ──MaintainAsync: ошибка──▶ Faulted (backoff растёт)
Faulted ──DisconnectAsync──▶ Disconnected
```

Важное уточнение: `DeviceDisabled` — это категория ошибки
(`MvkErrorKind.DeviceDisabled`), а не состояние. Устройство с `Enabled = false`
остаётся в `Disconnected` и никогда не участвует в реконнекте.

### Событие `MvkDeviceStateChangedEventArgs`

```csharp
public sealed class MvkDeviceStateChangedEventArgs : EventArgs
{
    public MvkDeviceStateChangedEventArgs(
        IMvkDevice device,
        MvkDeviceState oldState,
        MvkDeviceState newState,
        MvkModbusException? error = null);

    public IMvkDevice Device { get; }
    public MvkDeviceState OldState { get; }
    public MvkDeviceState NewState { get; }
    public MvkModbusException? Error { get; }
}
```

| Свойство | Описание |
|---|---|
| `Device` | Устройство, у которого изменилось состояние |
| `OldState` / `NewState` | Состояние до и после перехода |
| `Error` | Ошибка, вызвавшая переход (для `Connected` после успеха — `null`) |

Событие возникает только при смене состояния. Если состояние не изменилось,
но обновился `LastError`, событие не публикуется — для UI это означает, что
подписки на события недостаточно (см. «Подводные камни»).

### Подписка на уровне устройства

```csharp
IMvkDevice device = pool.GetDevice("mvk-1");

device.StateChanged += (_, args) =>
{
    Console.WriteLine($"{args.Device.Settings.Id}: {args.OldState} -> {args.NewState}");
    if (args.Error is not null)
    {
        Console.WriteLine($"  причина: {args.Error.ErrorKind} — {args.Error.Message}");
    }
};
```

### Подписка на уровне пула

```csharp
pool.DeviceStateChanged += (_, args) =>
{
    string state = $"{args.OldState} -> {args.NewState}";
    string error = args.Error is null ? "без ошибки" : args.Error.ErrorKind.ToString();
    Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss.fff} {args.Device.Settings.Id,-8} {state,-24} {error}");
};
```

Оба варианта получают одни и те же данные: пул просто пересылает события
устройств. Подписка на пул удобна, когда нужен единый журнал по всем МВК,
и не требует переподписки при `AddDevice`/`RemoveDevice`.

---

## 6. Жизненный цикл соединения

### Ленивое подключение

Соединение открывается при первом запросе:

```text
ExecuteAsync
  └─ SemaphoreSlim (очередь к устройству)
       └─ MvkPipeline: timeout → logging → metrics → validation → retry → writeProtection
            └─ NModbusTransport.ExecuteAsync
                 └─ if (!IsConnected) ConnectAsync()
                      └─ TcpClient.ConnectAsync(Host, Port) с ConnectTimeoutMs
```

Особенности ленивого пути:

- событие `Connecting` не публикуется (состояние меняется сразу
  `Disconnected` → `Connected`/`Faulted` по результату запроса);
- таймаут подключения (`ConnectTimeoutMs`) действует внутри транспорта;
- `RetryMiddleware` может повторить всю операцию (`MaxRetries` раз), то есть
  при недоступном устройстве один вызов читает до `MaxRetries + 1` попыток
  подключения, каждая со своим таймаутом и линейной задержкой `RetryDelayMs × попытка`.

### `EnsureConnectedAsync`

Явное подключение для проверки доступности:

```csharp
try
{
    await device.EnsureConnectedAsync(cts.Token);
    Console.WriteLine($"{device.Settings.Id}: связь установлена");
}
catch (MvkModbusException ex) when (ex.IsTransient)
{
    Console.WriteLine($"{device.Settings.Id}: недоступно ({ex.ErrorKind})");
}
```

При успехе состояние проходит `Connecting` → `Connected`, счётчик ошибок
сбрасывается, `LastError = null`. При временной ошибке состояние становится
`Faulted` и планируется реконнект; исключение выбрасывается вызывающему коду.
При отмене — состояние `Disconnected`, наружу уходит `OperationCanceledException`.

### Авто-разрыв при транспортной ошибке

Транспорт `NModbusTransport` при любой ошибке обращения (таймаут, разрыв,
ошибка сокета, ответ-исключение Modbus) закрывает TCP-соединение, а ошибку
возвращает как `ModbusResponse.Failure`. Поэтому сразу после ошибки
`device.IsConnected == false`, даже если состояние ещё не успело смениться.

`MvkDevice` по результату запроса (`ApplyResult`):

- успех — `MarkHealthy()` (сброс счётчика ошибок и `LastError`) и состояние
  `Connected` (либо `Disconnected`, если сокет закрыт);
- временная ошибка (`IsTransient`) — `Faulted` и планирование реконнекта;
- нетранзиентная ошибка (`DeviceError`, `Validation`, ...) — состояние
  `Connected`/`Disconnected` по факту, `LastError` заполняется. Если ошибку
  коротко замкнул middleware (например, `ValidationMiddleware` не дошёл
  до транспорта), состояние остаётся `Connected`, а сокет не закрывается.

### Реконнект с экспоненциальным backoff

При каждой временной ошибке счётчик подряд идущих ошибок `N` увеличивается,
и рассчитывается момент следующей попытки:

```text
delay_ms = min(ReconnectBaseDelayMs × 2^(N − 1), ReconnectMaxDelayMs)
```

где `N` — число временных ошибок подряд (первая ошибка даёт `N = 1`).

Со значениями по умолчанию (`ReconnectBaseDelayMs = 500`, `ReconnectMaxDelayMs = 30000`):

| `N` | Задержка до попытки, мс |
|---|---|
| 1 | 500 |
| 2 | 1 000 |
| 3 | 2 000 |
| 4 | 4 000 |
| 5 | 8 000 |
| 6 | 16 000 |
| 7 и далее | 30 000 (потолок) |

Сброс происходит при любом успехе: успешный запрос или успешный реконнект
обнуляют счётчик и `nextReconnectAt`.

Попытки выполняет `MaintainAsync` (фоновый цикл или ручной вызов): он
переводит `Faulted`-устройство в `Connecting` через `EnsureConnectedAsync`.
Поэтому фактическая задержка восстановления — это backoff **плюс** ожидание
ближайшего тика обслуживания (до `HealthCheckIntervalSeconds`).

`Faulted` не блокирует прикладные запросы: следующий `ExecuteAsync` сам
попробует подключиться лениво и при успехе вернёт состояние в `Connected`.

### Отключение простаивающих соединений

Idle-механизм закрывает соединение, если к устройству давно не было запросов:

- `IdleDisconnectSeconds` (по умолчанию 120) — порог простоя, `0` — не закрывать;
- время активности (`lastActivityTicks`) обновляется после завершения каждого
  запроса, включая неуспешный; `LastRequestAt` фиксируется в начале запроса;
- соединение закрывается только если оно реально открыто
  (`transport.IsConnected == true`);
- проверка выполняется в `MaintainAsync`, то есть с точностью до тика
  обслуживания; после закрытия состояние становится `Disconnected`.

```csharp
MvkDeviceOptions options = new()
{
    Id = "mvk-1",
    Host = "10.20.0.211"
};

MvkModbusOptions root = new()
{
    Defaults = new MvkDefaultsOptions
    {
        IdleDisconnectSeconds = 30,        // закрывать через 30 с простоя
        ReconnectBaseDelayMs = 500,
        ReconnectMaxDelayMs = 10000
    },
    Devices = [options]
};
```

Следующий запрос после idle-отключения просто откроет соединение заново —
для прикладного кода это прозрачно.

---

## 7. Сериализация запросов

Внутри `MvkDevice` — `SemaphoreSlim` на `Settings.MaxConcurrentRequests` слотов:

```csharp
requestGate = new SemaphoreSlim(
    Math.Max(1, settings.MaxConcurrentRequests),
    Math.Max(1, settings.MaxConcurrentRequests));
```

Что это значит:

- при `MaxConcurrentRequests = 1` (значение по умолчанию) все запросы к устройству
  выполняются строго по одному: второй вызов ждёт, пока завершится первый;
- при `MaxConcurrentRequests = k` одновременно через пайплайн и транспорт могут
  идти до `k` запросов;
- очередь ожидания не ограничена: если период опроса меньше времени выполнения
  запроса, вызовы накапливаются;
- ожидание в очереди прерывается `CancellationToken` вызывающего кода
  (`requestGate.WaitAsync(cancellationToken)`), при этом запрос ещё не отправлен;
- слот освобождается в `finally`, счётчик ошибок и `LastError` обновляются
  до освобождения слота.

```csharp
// Параллельные вызовы безопасны: второй встанет в очередь
await Task.WhenAll(
    device.ReadHoldingRegistersAsync(0x1004, 2),
    device.ReadHoldingRegistersAsync(0x2000, 2));
```

Когда имеет смысл `MaxConcurrentRequests > 1`:

- несколько независимых операций должны уложиться в общий дедлайн, и потери
  на ожидании недопустимы;
- транспорт гарантированно потокобезопасен, а устройство выдерживает
  параллельную обработку.

Для МВК01 и штатного `NModbusTransport` рекомендуется `1`:

- один объект устройства — это одно TCP-соединение; параллельные транзакции
  по одному сокету зависят от потокобезопасности реализации транспорта;
- один DSP устройства обрабатывает запросы последовательно: параллелизм на
  клиенте не ускорит ответы, а только создаст конкуренцию за канал;
- диагностика и трассировка при одном потоке запросов заметно проще.

Валидатор допускает `MaxConcurrentRequests` в диапазоне 1–64; значение вне
диапазона отклоняется при загрузке конфигурации.
Практический ориентир: если появляется мысль увеличить значение, сначала
проверьте, нельзя ли объединить запросы (например, читать больше регистров
за один вызов) или разнести опрос по времени.

---

## 8. Управление составом пула

### Добавление на лету

`AddDevice` принимает `MvkDeviceOptions` и применяет к нему `Options.Defaults`:

```csharp
IMvkDevice backup = pool.AddDevice(new MvkDeviceOptions
{
    Id = "mvk-backup",
    Name = "Резерв",
    Host = "10.20.0.214",
    Enabled = true,
    Tags = new Dictionary<string, string> { ["asset"] = "Резервный стенд" }
});

// доступно всем потребителям пула
IMvkDevice same = pool.GetDevice("mvk-backup");
```

Что происходит внутри:

1. `deviceOptions.Resolve(Options.Defaults)` — наследуются порт, `SlaveId`,
   `ByteOrder`, таймауты, ретраи, реконнект, idle-порог, политика записи;
2. проверка непустого `Id` — иначе `MvkModbusException` (`Configuration`);
3. создание транспорта и пайплайна;
4. регистронезависимая проверка уникальности: дубликат — `MvkModbusException`
   (`Configuration`);
5. подписка на `StateChanged` устройства и логирование факта добавления.

Подключение при `AddDevice` не выполняется: первый запрос подключится лениво,
а `EnsureConnectedAsync` позволяет проверить доступность сразу.

### Удаление

```csharp
IMvkDevice temporary = pool.AddDevice(new MvkDeviceOptions
{
    Id = "mvk-temp",
    Host = "10.20.0.215"
});

ushort[] data = await temporary.ReadHoldingRegistersAsync(0x1004, 2);

if (pool.RemoveDevice("mvk-temp"))
{
    Console.WriteLine("Временное устройство удалено");
}
```

`RemoveDevice` возвращает `true` только если устройство реально было в пуле.
После удаления объект освобождён: обращаться к нему нельзя, а поиск по `Id`
даёт `false`/`DeviceNotFound`.

Типовой сценарий «устройство пришло с объекта»:

```csharp
public async Task RegisterAsync(IMvkDevicePool pool, string id, string host, CancellationToken ct)
{
    if (!pool.TryGetDevice(id, out IMvkDevice device))
    {
        device = pool.AddDevice(new MvkDeviceOptions { Id = id, Host = host });
    }

    await device.EnsureConnectedAsync(ct);
}
```

Типовой сценарий «устройство вывели из эксплуатации»:

```csharp
if (pool.TryGetDevice(id, out IMvkDevice device) && device.IsConnected)
{
    await device.DisconnectAsync();
}

pool.RemoveDevice(id);
```

---

## 9. Отключённые устройства

`Enabled = false` — способ держать устройство в конфигурации, не опрашивая его:

```csharp
MvkModbusOptions options = new()
{
    Devices =
    [
        new MvkDeviceOptions { Id = "mvk-1", Host = "10.20.0.211" },
        new MvkDeviceOptions { Id = "mvk-2", Host = "10.20.0.212", Enabled = false }
    ]
};

await using MvkDevicePool pool = new(options);

IMvkDevice disabled = pool.GetDevice("mvk-2");
Console.WriteLine(disabled.State);        // Disconnected
Console.WriteLine(disabled.Settings.Enabled); // False
```

Поведение отключённого устройства:

| Действие | Результат |
|---|---|
| Видно в `pool.Devices` | Да, наравне с остальными |
| `GetDevice`/`TryGetDevice` | Работают |
| `ExecuteAsync` | `ModbusResponse.Failure` с `MvkErrorKind.DeviceDisabled`; сокет не открывается |
| Обёртки чтения/записи | `throw MvkModbusException` (`DeviceDisabled`) |
| `EnsureConnectedAsync` | `throw MvkModbusException` (`DeviceDisabled`) |
| `State` | Остаётся `Disconnected` |
| Фоновый реконнект | Не выполняется (`ShouldReconnect` проверяет `Settings.Enabled`) |
| `LastRequestAt` | Не обновляется (проверка `Enabled` идёт до отметки времени) |
| `DisconnectAsync` | Выполняется (закрывает уже открытое соединение, если оно было) |

Проверка `Enabled` выполняется до постановки запроса в очередь, поэтому
отключение устройства в конфигурации — дешёвый способ исключить его из опроса.
Значение `Enabled` фиксируется при создании устройства: чтобы включить его
обратно, удалите устройство (`RemoveDevice`) и добавьте заново (`AddDevice`)
или пересоздайте пул.

---

## 10. Практические примеры

### Пример 1. Два устройства и чтение с обоих

```csharp
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Pool;

MvkModbusOptions options = new()
{
    Pool = new MvkPoolOptions { ReconnectEnabled = true, HealthCheckIntervalSeconds = 5 },
    Devices =
    [
        new MvkDeviceOptions { Id = "mvk-1", Name = "Стенд 1", Host = "10.20.0.211" },
        new MvkDeviceOptions { Id = "mvk-2", Name = "Стенд 2", Host = "10.20.0.212" }
    ]
};

await using MvkDevicePool pool = new(options);

IMvkDevice first = pool.GetDevice("mvk-1");
IMvkDevice second = pool.GetDevice("mvk-2");

Task<ushort[]> firstRead = first.ReadHoldingRegistersAsync(0x1004, 2);
Task<ushort[]> secondRead = second.ReadHoldingRegistersAsync(0x1004, 2);

ushort[][] versions = await Task.WhenAll(firstRead, secondRead);

Console.WriteLine($"mvk-1 — ПО {versions[0][0]:X4} {versions[0][1]:X4}");
Console.WriteLine($"mvk-2 — ПО {versions[1][0]:X4} {versions[1][1]:X4}");
```

Запросы к разным устройствам действительно идут параллельно (у каждого свой
сокет и свой семафор); внутри одного устройства они сериализуются.

### Пример 2. Фоновый мониторинг всех устройств с журналом событий

```csharp
using System.Collections.Concurrent;
using Mvk.Modbus;
using Mvk.Modbus.Pool;

ConcurrentQueue<string> journal = new();

pool.DeviceStateChanged += (_, args) =>
{
    string error = args.Error is null
        ? string.Empty
        : $" | {args.Error.ErrorKind}: {args.Error.Message}";

    journal.Enqueue(
        $"{DateTimeOffset.Now:HH:mm:ss.fff} {args.Device.Settings.Id}: " +
        $"{args.OldState} -> {args.NewState}{error}");
};

using CancellationTokenSource cts = new();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

using PeriodicTimer timer = new(TimeSpan.FromSeconds(2));

while (await timer.WaitForNextTickAsync(cts.Token))
{
    foreach (IMvkDevice device in pool.Devices)
    {
        try
        {
            ushort[] data = await device.ReadHoldingRegistersAsync(0x1004, 2, cts.Token);
            Console.WriteLine(
                $"{DateTime.Now:HH:mm:ss} {device.Settings.Id,-8} ПО {data[0]:X4} {data[1]:X4}");
        }
        catch (MvkModbusException ex)
        {
            Console.WriteLine(
                $"{DateTime.Now:HH:mm:ss} {device.Settings.Id,-8} недоступно: {ex.ErrorKind}");
            // реконнект уже выполняется фоновым циклом
        }
    }

    while (journal.TryDequeue(out string? line))
    {
        Console.WriteLine($"  событие: {line}");
    }
}
```

`PeriodicTimer` не накапливает дрейф, а `ConcurrentQueue` безопасен для
обработчика события, который приходит из фоновых потоков.

### Пример 3. «Мягкая» обработка недоступности

Обёртка, превращающая временные ошибки в отсутствие данных:

```csharp
private static async Task<ushort[]?> TryReadAsync(
    IMvkDevice device,
    ushort address,
    ushort quantity,
    CancellationToken cancellationToken)
{
    try
    {
        return await device.ReadHoldingRegistersAsync(address, quantity, cancellationToken);
    }
    catch (OperationCanceledException)
    {
        throw;   // отмена — не ошибка связи
    }
    catch (MvkModbusException ex) when (ex.IsTransient)
    {
        return null;   // связь: реконнект уже идёт в фоне
    }
}
```

Использование:

```csharp
ushort[]? data = await TryReadAsync(device, 0x1004, 2, cancellationToken);

if (data is null)
{
    Console.WriteLine($"{device.Settings.Id}: данные недоступны, " +
                      $"последняя ошибка: {device.LastError?.ErrorKind}");
}
else
{
    Console.WriteLine($"ПО: {data[0]:X4} {data[1]:X4}");
}
```

Нетранзиентные ошибки (`DeviceError`, `Validation`) намеренно не перехватываются:
это ошибки данных или конфигурации, их нужно исправлять, а не ждать реконнекта.

### Пример 4. Сводка состояния для UI

```csharp
public sealed record MvkDeviceStatus(
    string Id,
    string Endpoint,
    MvkDeviceState State,
    bool IsConnected,
    DateTimeOffset LastRequestAt,
    string? LastError);

private static IReadOnlyList<MvkDeviceStatus> BuildStatusSnapshot(IMvkDevicePool pool)
{
    return pool.Devices
        .Select(device => new MvkDeviceStatus(
            device.Settings.Id,
            device.Settings.Endpoint,
            device.State,
            device.IsConnected,
            device.LastRequestAt,
            device.LastError?.Message))
        .ToList();
}
```

Периодический вывод:

```csharp
foreach (MvkDeviceStatus status in BuildStatusSnapshot(pool))
{
    Console.WriteLine(
        $"{status.Id,-8} {status.Endpoint,-18} {status.State,-12} " +
        $"{(status.IsConnected ? "связь" : "нет связи"),-9} " +
        $"последний запрос: {status.LastRequestAt:HH:mm:ss} " +
        $"{status.LastError}");
}
```

Если сводка идёт в WPF-приложение: храните `ObservableCollection<MvkDeviceStatus>`
во ViewModel, обновляйте её из `DeviceStateChanged` и по таймеру, а изменения
привязок выполняйте в UI-потоке (`Dispatcher.InvokeAsync`). Помните, что
`LastError` может обновиться без события (состояние не менялось), поэтому
периодическое обновление снимка обязательно.

---

## 11. Производительность и масштаб

### Сколько устройств держать

- Пул на `N` устройств создаёт `N` объектов `MvkDevice` с пайплайном и
  транспортом, но **ни одного сокета**, пока к устройству не пришёл запрос.
  Стоимость «спящего» устройства — только память.
- Сокет (файловый дескриптор) занимает лишь подключённое устройство.
  Idle-отключение возвращает дескриптор через `IdleDisconnectSeconds`.
- Один фоновый цикл обслуживания — одна задача на весь пул. Проход по списку
  дешёвый, но выполняется последовательно: `MaintainAsync` обходит все
  устройства по очереди. Для пулов в сотни устройств не ставьте
  `HealthCheckIntervalSeconds = 1` без необходимости.
- Практический ориентир: сотни устройств в конфигурации — норма; десятки
  одновременно подключённых — тоже, если сеть и МВК справляются с трафиком.
  Верхняя граница определяется не пулом, а количеством дескрипторов процесса
  и лимитами сети.

### Почему idle-отключение экономит ресурсы

- закрывается TCP-соединение: освобождается сокет клиента (дескриптор) и
  входящее соединение на стороне МВК01;
- уменьшается объём служебного трафика: нет keep-alive и случайных ошибок
  на «мёртвых» соединениях;
- устройство, опрашиваемое раз в час, не держит соединение 59 минут из 60.

Цена: следующий запрос после паузы платит за `ConnectAsync` (единицы–десятки
миллисекунд в локальной сети). Если период опроса меньше `IdleDisconnectSeconds`,
соединение вообще не будет закрываться.

### Как выбирать `HealthCheckIntervalSeconds`

| Интервал | Плюсы | Минусы |
|---|---|---|
| 1–5 с | Быстрое восстановление после обрыва, точное idle-отключение | Частые проходы по всем устройствам |
| 15 с (по умолчанию) | Хороший баланс для промышленной сети | Восстановление с точностью до 15 с |
| 60 с и больше | Минимум фоновой работы | Долгое ожидание реконнекта и позднее закрытие простаивающих соединений |

Фактическое время восстановления ≈ `backoff(N) + до одного интервала тика`.
Если нужен быстрый реконнект (1–2 с), уменьшайте интервал, а не base delay:
сам backoff тоже должен оставаться разумным, чтобы не «долбить» выключенное
устройство.

### Почему опрос с периодом 0,5 с безопасен

- запросы к одному устройству сериализуются семафором: в один момент времени
  через сокет идёт ровно один запрос, наложения и перемешивания ответов нет;
- типичное чтение 2–16 регистров в локальной сети занимает единицы миллисекунд,
  поэтому очередь из одного-двух запросов не растёт;
- данные в МВК01 обновляются DSP-буфером, повторная отправка запроса не нужна
  (см. README, раздел «Периодический опрос значений»);
- если запрос всё же «завис» (например, сработал `ReadTimeoutMs = 1000`),
  следующая итерация таймера встанет в очередь и выполнится после освобождения
  слота — потери запросов нет, но период на это время растянется.

При одном опрашивающем цикле на устройство очередь фактически нулевая.
Несколько независимых циклов на одно устройство тоже корректны, но
бессмысленны: они будут ждать друг друга.

### Масштабирование

- Пул в DI — singleton: все потребители делят один набор устройств и сокетов.
- `pool.Devices` копирует коллекцию при каждом обращении — вызывайте редко,
  кэшируйте результат, если состав стабилен.
- `ExecuteAsync` без обёрток выделяет `ModbusRequestContext`, словарь `Items`
  и `ModbusResponse` на каждый запрос — при опросе 2 Гц это несущественно;
  для более высоких частот сначала измерьте, потом оптимизируйте.
- Ошибки связи дешевле предотвращать: не запускайте опрос отключённых
  устройств (`Enabled = false`), не держите неиспользуемые устройства
  подключёнными (idle-порог), разносите опрос разных устройств по времени.

---

## 12. Ограничения

1. **Один объект устройства — одно соединение и один `SlaveId`.**
   Если на одном IP живут несколько Modbus-устройств (разные `SlaveId`),
   создайте для каждого отдельный `MvkDeviceOptions` с тем же `Host` и своим
   `SlaveId`: у каждого будет свой сокет.
2. **Нет приоритетов запросов.** Все вызовы встают в одну очередь семафора;
   «важный» запрос не обгонит «фоновый». Если нужен приоритет, управляйте
   порядком вызовов на уровне приложения.
3. **Очередь ожидания не ограничена.** Долгие запросы при малом периоде опроса
   накапливают ожидающие вызовы; ограничение — только `CancellationToken`.
4. **`MaintainAsync` можно и нужно вызывать вручную**, когда
   `ReconnectEnabled = false`: так работают unit-тесты (см.
   `tests/Mvk.Modbus.Tests/Pool/MvkDeviceTests.cs`) и приложения со своим
   планировщиком.
5. **`Enabled` не меняется на лету.** Значение фиксируется в `MvkDeviceSettings`
   при создании устройства; включение/выключение — через `RemoveDevice` +
   `AddDevice` или пересоздание пула.
6. **`AddDevice` использует только `Defaults`.** В `MvkDeviceOptions` нет полей
   таймаутов, ретраев, idle-порога и backoff: эти параметры берутся из
   `Options.Defaults` и не могут быть заданы для отдельного устройства на лету.
7. **`AddDevice` не подключается.** Доступность проверяется запросом или
   `EnsureConnectedAsync`.
8. **Пул не проверяет живость подключённых устройств.** Фоновый цикл
   реконнектит только `Faulted` и закрывает простаивающие; «мёртвый, но
   открытый» сокет обнаружится при следующем реальном запросе.
9. **Пул не дробит большие запросы.** Лимит Modbus — 125 регистров за чтение
   (`ModbusRequest.MaxReadRegisters`) и 123 за запись; разбиение на чанки —
   задача вышележащего слоя (типизированный слой МВК01 делает это сам).
10. **Программная конфигурация не проходит валидатор.** При
    `new MvkDevicePool(options)` дубликаты `Id` молча схлопываются в первое
    устройство, а некорректные диапазоны не проверяются (валидатор включён
    в DI-путь и в `MvkModbusOptionsLoader`). Проверяйте конфигурацию заранее.
11. **Удаление устройства во время активных запросов — гонка.** Дождитесь
    завершения запросов, затем вызывайте `RemoveDevice`.
12. **Пул не ждёт незавершённые запросы при `DisposeAsync`.** Освобождайте пул
    после остановки опроса: запросы, попавшие в момент освобождения, могут
    завершиться `ObjectDisposedException`.

---

## Подводные камни

1. **Не освобождайте устройства из пула.** `IMvkDevice : IDisposable`, но
   владелец — пул. Вызов `device.Dispose()` сломает пул: устройство останется
   в `Devices`, а его сокет и семафор будут освобождены. Удаляйте через
   `RemoveDevice`.
2. **`GetDevice` бросает исключение.** Для пользовательских идентификаторов
   используйте `TryGetDevice`. И помните: пустая строка — `ArgumentException`
   в обоих методах, а не «не найдено».
3. **Идентификаторы регистронезависимы.** `mvk-1` и `MVK-1` — одно устройство;
   `AddDevice` с другим регистром считается дубликатом.
4. **`ReconnectEnabled = false` отключает не только реконнект, но и
   idle-отключение** — оба механизма живут в цикле обслуживания. Без цикла
   вызывайте `MaintainAsync` вручную.
5. **Backoff применяется на тике обслуживания.** Даже при `ReconnectBaseDelayMs = 500`
   восстановление может занять `500 мс + до HealthCheckIntervalSeconds`.
6. **`Faulted` не «замораживает» устройство.** Следующий `ExecuteAsync` сам
   попробует подключиться, не дожидаясь backoff; backoff управляет только
   фоновыми попытками.
7. **`LastError` меняется без события.** Если состояние не изменилось,
   `StateChanged`/`DeviceStateChanged` не публикуются, хотя `LastError` уже
   обновлён. Для UI обновляйте снимок по таймеру.
8. **Нетранзиентные ошибки не лечат реконнектом.** `DeviceError` (ответ
   устройства с кодом Modbus) и `Validation` не переводят устройство в `Faulted`
   и не планируют backoff — их нужно обрабатывать в прикладном коде.
9. **Ошибка транспорта закрывает сокет.** После любой ошибки обращения
   `IsConnected == false`, даже если `State` ещё `Connected` (или уже
   `Disconnected` при нетранзиентной ошибке). Не полагайтесь на `IsConnected`
   как на «связь гарантированно есть» — это лишь состояние сокета.
10. **Обработчики событий выполняются синхронно в потоке запроса/обслуживания.**
    Не бросайте из них исключения (исключение обработчика `StateChanged`
    вылетит из `ExecuteAsync` вместо `ModbusResponse`) и не делайте тяжёлых
    операций. Для UI маршалите в `Dispatcher`.
11. **Не удаляйте устройство из обработчика события и во время запросов.**
    `RemoveDevice` освобождает транспорт и семафор; активный запрос к этому
    устройству завершится ошибкой.
12. **`Devices` — новый список при каждом обращении.** Не вызывайте в горячем
    цикле; кэшируйте, если состав пула стабилен.
13. **Изменение `Options` после создания пула ни на что не влияет.**
    Параметры устройств уже развёрнуты в `MvkDeviceSettings`, а интервал
    обслуживания прочитан при старте цикла. Меняйте конфигурацию до создания
    пула или пересоздавайте пул.
14. **`MaxConcurrentRequests > 1` — осознанное решение.** Один сокет,
    один DSP и один `SlaveId` на устройство; параллелизм меняет только
    очередность ожидания, а не пропускную способность канала. Для МВК01
    оставляйте значение по умолчанию `1`.
15. **Синхронный `Dispose()` блокирует поток.** В UI-приложениях используйте
    `await using`/`DisposeAsync`, а не `Dispose()` — иначе легко получить
    подвисание при закрытии окна.
16. **`ConnectTimeoutMs` и `ExecuteAsync(request, timeout)` — разные вещи.**
    Первый ограничивает установку TCP-соединения, второй — всю операцию целиком
    (`TimeoutMiddleware`). Для медленных операций (Save/Reboot) увеличивайте
    общий таймаут, а не сокетный.

---

## См. также

- [Конфигурация](03-configuration.md) — секция `MvkModbus`, `Defaults`, `Devices`,
  `Pool`, валидация JSON.
- [Пайплайн](06-pipeline-middleware.md) — middleware, через который проходит
  каждый запрос устройства, короткое замыкание, ретраи и таймауты.
- [README](../README.md) — быстрый старт, состояния и события, обработка ошибок,
  периодический опрос.
- `tests/Mvk.Modbus.Tests/Pool/MvkDevicePoolTests.cs`,
  `tests/Mvk.Modbus.Tests/Pool/MvkDeviceTests.cs` — эталонные примеры
  (ручной `MaintainAsync`, `FakeTransport`, события, сериализация запросов).
