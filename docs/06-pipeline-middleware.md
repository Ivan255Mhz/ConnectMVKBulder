# Глава 06. Пайплайн обработки запросов (middleware)

Каждый запрос к МВК01 — это не только вызов сокета. Вокруг обращения к устройству нужно
решить несколько сквозных задач: проверить запрос до выхода в сеть, применить политику
записи, повторить операцию при временном сбое, уложиться в бюджет времени, записать лог и
метрики. Библиотека решает их цепочкой middleware — тем же приёмом, что и ASP.NET Core:
запрос проходит через конвейер обработчиков, каждый из которых может выполнить код до и
после следующего, заменить токен отмены, изменить ответ или вовсе не пускать запрос дальше.

Глава описывает контракты пайплайна, встроенные middleware, порядок их включения,
регистрацию своих обработчиков и ручную сборку без DI.

---

## 1. Зачем нужен пайплайн и как он работает

Пайплайн — это набор независимых блоков, нанизанных на одну операцию «запрос → ответ».
Каждый блок отвечает ровно за одну задачу и не знает о существовании остальных:

- **timeout** — ограничивает общее время операции;
- **logging**, **metrics** — наблюдение;
- **validation**, **writeProtection** — допуск запроса до устройства;
- **retry** — повтор временных сбоев;
- пользовательские блоки — аудит, кэш, ограничение частоты и всё, что нужно приложению.

Типовой запрос проходит через все блоки; аварийный (неверный адрес, запрет записи) может
быть остановлен на середине, так и не дойдя до сокета. Благодаря этому проверки и политики
живут в одном месте, а не размазаны по вызывающему коду.

### 1.1. Схема прохождения запроса

Порядок по умолчанию (первый middleware — самый внешний):

```text
IMvkDevice.ExecuteAsync(request, timeout?, ct)
   │
   │  MvkDevice: проверка Enabled → requestGate → создание ModbusRequestContext
   ▼
┌─ TimeoutMiddleware ─────────────────────────────────────────────┐
│ ┌─ LoggingMiddleware ─────────────────────────────────────────┐ │
│ │ ┌─ MetricsMiddleware ─────────────────────────────────────┐ │ │
│ │ │ ┌─ ValidationMiddleware ──────────────────────────────┐ │ │ │
│ │ │ │ ┌─ RetryMiddleware ───────────────────────────────┐ │ │ │ │
│ │ │ │ │ ┌─ WriteProtectionMiddleware ─────────────────┐ │ │ │ │ │
│ │ │ │ │ │   терминал: TerminalFromTransport           │ │ │ │ │ │
│ │ │ │ │ │      → IMvkTransport.ExecuteAsync           │ │ │ │ │ │
│ │ │ │ │ │      → NModbusTransport → TCP-сокет         │ │ │ │ │ │
│ │ │ │ │ └─────────────────────────────────────────────┘ │ │ │ │ │
│ │ │ │ └─────────────────────────────────────────────────┘ │ │ │ │
│ │ │ └─────────────────────────────────────────────────────┘ │ │ │
│ │ └─────────────────────────────────────────────────────────┘ │ │
│ └─────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
   │  context.Response заполнен (успех или ошибка)
   ▼
MvkDevice.ApplyResult → возврат ModbusResponse вызывающему коду
```

Линейная запись того же порядка:

```text
timeout → logging → metrics → validation → retry → writeProtection → транспорт
```

Важно, что это не «список вызовов», а вложенные друг в друга обёртки. После того как
терминал вернул управление, код middleware выполняется в обратном порядке:
`writeProtection → retry → validation → metrics → logging → timeout`. Поэтому middleware
может измерить длительность, посмотреть итоговый `context.Response` и что-то с ним сделать.

### 1.2. Роль терминального обработчика

Цепочку замыкает терминальный обработчик (terminal) — делегат, который реально обращается
к устройству. Для этого есть готовый статический метод:

```csharp
public static InvokeModbusDelegate TerminalFromTransport(IMvkTransport transport)
```

Он вызывает `transport.ExecuteAsync(context.Request, cancellationToken)` и записывает
результат в `context.Response`. Никакой другой обработчик не имеет права работать с
сокетом — это единственная точка, где запрос покидает библиотеку.

Если пайплайн завершился, а `context.Response` так и остался `null` (например, свой
middleware не вызвал `next` и забыл заполнить ответ), `MvkPipeline.ExecuteAsync` бросает
`MvkModbusException` с `MvkErrorKind.Unknown` и текстом «Пайплайн завершился без ответа».

### 1.3. Где живёт пайплайн

Пайплайн создаётся на каждое устройство, а middleware — нет. При добавлении устройства в пул
выполняется примерно следующее:

```csharp
IMvkTransport transport = transportFactory.Create(settings);
MvkPipeline pipeline = new(
    MvkPipeline.TerminalFromTransport(transport),
    middlewares);                                  // один и тот же список для всех устройств

MvkDevice device = new(settings, transport, pipeline, logger);
```

Отсюда два следствия:

| Что | Где создаётся | Сколько экземпляров |
|---|---|---|
| `MvkPipeline` | `MvkDevicePool.AddCore` (или вручную) | один на устройство |
| Терминал (`IMvkTransport`) | фабрика транспортов | один на устройство |
| `IModbusMiddleware` | `MvkPipelineFactory` / `MvkMiddlewareResolver` / вручную | один на весь пул (общие для устройств) |
| `ModbusRequestContext` | `MvkDevice.ExecuteCoreAsync` | один на запрос |

Значит, middleware **обязаны быть потокобезопасными**: экземпляр `LoggingMiddleware` или
вашего `AuditMiddleware` одновременно используется всеми устройствами пула. Никакого
изменяемого состояния в полях держать нельзя — состояние запроса живёт только в
`ModbusRequestContext` (точнее, в `context.Items`).

Дополнительно: у каждого устройства есть `SemaphoreSlim requestGate`, ограничивающий
число одновременных запросов значением `MaxConcurrentRequests` (по умолчанию 1). Но
гарантировать «один запрос на устройство» пайплайн не может и не должен — за это отвечает
шлюз в `MvkDevice`.

---

## 2. Контракты пайплайна

### 2.1. `InvokeModbusDelegate`

```csharp
public delegate Task InvokeModbusDelegate(
    ModbusRequestContext context,
    CancellationToken cancellationToken);
```

Это «следующий обработчик»: middleware получает его параметром и вызывает, когда хочет
передать управление дальше. Никакого возвращаемого значения у делегата нет — результат
работы накапливается в `context.Response`. Именно поэтому возможны короткие замыкания,
замена токена и обработка «после next».

### 2.2. `IModbusMiddleware`

```csharp
public interface IModbusMiddleware
{
    Task InvokeAsync(
        ModbusRequestContext context,
        InvokeModbusDelegate next,
        CancellationToken cancellationToken);
}
```

Три параметра и ничего лишнего:

| Параметр | Что это |
|---|---|
| `context` | Контекст запроса: настройки, запрос, ответ, `Items`, номер попытки |
| `next` | Следующий обработчик пайплайна (вплоть до терминала) |
| `cancellationToken` | Токен отмены, действующий на этом уровне; можно заменить своим |

Стандартный «прозрачный» middleware выглядит так:

```csharp
public Task InvokeAsync(ModbusRequestContext context, InvokeModbusDelegate next, CancellationToken cancellationToken)
{
    return next(context, cancellationToken);
}
```

### 2.3. `ModbusRequestContext` — полный состав

Контекст — аналог `HttpContext` в ASP.NET. Создаётся на один запрос и живёт до его конца.

Конструктор:

```csharp
public ModbusRequestContext(
    MvkDeviceSettings settings,
    ModbusRequest request,
    CancellationToken aborted)
```

| Член | Тип | Доступ | Назначение |
|---|---|---|---|
| `Settings` | `MvkDeviceSettings` | get | Параметры устройства с применёнными `Defaults`: `Id`, `Host`, `Port`, `SlaveId`, `MaxRetries`, `RetryDelayMs`, `WritePolicy`, `ProtectedWriteStartAddress/EndAddress`, `RequireEvenAddressAndQuantity` и т.д. |
| `Request` | `ModbusRequest` | get | Иммутабельный запрос: `DeviceId`, `Operation`, `StartAddress`, `Quantity`, `WriteData`, вычисляемые `IsWrite` и `EndAddress` |
| `Response` | `ModbusResponse?` | get/set | Ответ. Заполняет терминал; middleware заполняет при коротком замыкании. Пока `null` — ответа ещё нет |
| `Items` | `IDictionary<string, object?>` | get | Произвольные данные, которыми middleware обмениваются в рамках одного запроса. Словарь с `StringComparer.Ordinal`, создаётся в конструкторе контекста |
| `Attempt` | `int` | get/set | Номер текущей попытки. Начальное значение — 1; увеличивает `RetryMiddleware` |
| `Aborted` | `CancellationToken` | get | Токен отмены **вызывающего кода**, переданный в конструктор. Именно его `MvkPipeline.ExecuteAsync` передаёт в голову цепочки |
| `IsCompleted` | `bool` | get | Вычисляемое: `Response is not null`. Удобно для проверок «уже сформирован ответ» |
| `TryGetItem<T>(string key, out T value)` | `bool` | — | Достаёт значение из `Items`. Возвращает `true`, только если значение есть и его тип **точно** `T` (никаких преобразований `int` ↔ `long`); иначе `false`, а `value` = `default` |

Пример работы с контекстом:

```csharp
if (context.TryGetItem<int>(MvkContextItems.TimeoutMs, out int timeoutMs))
{
    // timeoutMs задан вызывающим кодом
}

if (context.IsCompleted)
{
    // ответ уже сформирован — дальше идти незачем
}
```

### 2.4. Правила работы с контекстом

**Короткое замыкание (short-circuit).** Middleware может вообще не вызывать `next`, а
сразу сформировать ответ:

```csharp
context.Response = ModbusResponse.Failure(new MvkModbusException(
    "Запрос отклонён политикой приложения.", MvkErrorKind.Validation, context.Settings.Id));
return Task.CompletedTask;
```

Терминал и все middleware, стоящие ниже, не выполняются. Ответ должен быть ненулевым —
иначе `MvkPipeline.ExecuteAsync` бросит `MvkModbusException(Unknown)`. Если ответ
получен без обращения к устройству, полезно пометить это в `Items` (см. раздел 3):

```csharp
context.Response = ModbusResponse.Success(cached);
context.Items[MvkContextItems.ShortCircuited] = true;
return Task.CompletedTask;
```

**Замена токена для вложенных обработчиков.** Нельзя «изменить» токен, пришедший
параметром, но можно передать в `next` другой:

```csharp
using CancellationTokenSource linked =
    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
linked.CancelAfter(TimeSpan.FromSeconds(1));

await next(context, linked.Token).ConfigureAwait(false);
```

Так устроен `TimeoutMiddleware`. Связанный токен действует на все вложенные middleware и
терминал; по выходу из блока `using` источник освобождается. Никогда не создавайте
`CancellationTokenSource` без `using` — это утечка таймеров.

**Изменение запроса невозможно.** `context.Request` — свойство только для чтения, а сам
`ModbusRequest` иммутабелен (приватный конструктор, фабричные методы, только get-свойства).
Подменить запрос «на лету» нельзя — это осознанное решение: запрос валидируется один раз
при создании, и все middleware видят один и тот же объект. Если middleware нужно другое
обращение к устройству, варианты такие:

1. отклонить запрос с ошибкой (`Validation`) и выполнить нужное действие на уровне выше;
2. сформировать собственный `ModbusRequest` и выполнить его через `IMvkDevice`/пул
   (например, разбить длинное чтение на части) — но помните о риске рекурсии: такой
   запрос снова пройдёт через весь пайплайн, включая этот middleware;
3. вынести трансформацию в вызывающий код или типизированный слой.

Не пытайтесь «обойти» иммутабельность отражением или пересозданием контекста — это ломает
инварианты валидации и защиты записи.

---

## 3. `MvkContextItems` — ключи для `Items`

```csharp
public static class MvkContextItems
{
    public const string TimeoutMs = "mvk.timeoutMs";         // int
    public const string ShortCircuited = "mvk.shortCircuited"; // bool
}
```

| Ключ | Тип значения | Кто использует |
|---|---|---|
| `TimeoutMs` | `int`, миллисекунды | Читает `TimeoutMiddleware`; устанавливает `MvkDevice` или вызывающий код |
| `ShortCircuited` | `bool` | Соглашение для пользовательских middleware: «ответ получен без обращения к устройству». Встроенные middleware его не читают и не пишут |

### Таймаут операции из кода

Самый простой способ — перегрузка `IMvkDevice.ExecuteAsync` с `TimeSpan`:

```csharp
ModbusResponse response = await device.ExecuteAsync(
    ModbusRequest.ReadHoldingRegisters(settings.Id, 0x1004, 2),
    TimeSpan.FromSeconds(3),
    cancellationToken);
```

`MvkDevice` сам положит таймаут в контекст (фактически выполняется
`context.Items[MvkContextItems.TimeoutMs] = (int)timeout.Value.TotalMilliseconds`), и
`TimeoutMiddleware` увидит его. Ровно так работает типизированный слой: команда
`SaveConfiguration` может отвечать до трёх секунд, поэтому
`Mvk01Device.ExecuteSystemCommandAsync` подставляет `DefaultCommandTimeout = 3 с`.

Эквивалент при ручном управлении контекстом:

```csharp
ModbusRequestContext context = new(settings, request, cancellationToken);
context.Items[MvkContextItems.TimeoutMs] = 3000;   // общий бюджет операции

ModbusResponse response = await pipeline.ExecuteAsync(context);
```

Правила, о которых легко забыть:

- `TryGetItem<int>` вернёт `false`, если положить `long`, `TimeSpan` или строку;
- значения `<= 0` для `TimeoutMs` означают «таймаута нет» — `TimeoutMiddleware` просто
  пропускает запрос дальше, и действуют только сокетные таймауты транспорта;
- ключ `TimeoutMs` **не** заменяет `ReadTimeoutMs`/`WriteTimeoutMs`: это разные уровни
  (см. раздел 10).

Проверка признака короткого замыкания в своём middleware:

```csharp
if (context.TryGetItem<bool>(MvkContextItems.ShortCircuited, out bool shortCircuited) && shortCircuited)
{
    // ответ взят из кэша или сформирован без устройства
}
```

---

## 4. Сборка пайплайна

### 4.1. `MvkPipelineBuilder`

Сборщик цепочки. Добавляет middleware по одному и в конце собирает пайплайн с терминалом.

```csharp
public sealed class MvkPipelineBuilder
{
    public IReadOnlyList<IModbusMiddleware> Middlewares { get; }

    public MvkPipelineBuilder Use(IModbusMiddleware middleware);

    public MvkPipelineBuilder Use(
        Func<ModbusRequestContext, InvokeModbusDelegate, CancellationToken, Task> middleware);

    public MvkPipeline Build(InvokeModbusDelegate terminal);
}
```

| Метод | Поведение |
|---|---|
| `Use(IModbusMiddleware)` | Добавляет экземпляр в конец списка. `null` → `ArgumentNullException` |
| `Use(Func<...>)` | Оборачивает лямбду в приватный `DelegateMiddleware` и вызывает первый `Use`. Удобно для одноразовых блоков и тестов |
| `Build(InvokeModbusDelegate)` | Создаёт `MvkPipeline` с указанным терминалом. `null` → `ArgumentNullException` |
| `Middlewares` | Текущий список в порядке выполнения (первый — самый внешний) |

Пример с лямбдой:

```csharp
MvkPipeline pipeline = new MvkPipelineBuilder()
    .Use(new ValidationMiddleware())
    .Use((context, next, ct) =>
    {
        Console.WriteLine($"Запрос: {context.Request.Operation} 0x{context.Request.StartAddress:X4}");
        return next(context, ct);
    })
    .Use(new RetryMiddleware())
    .Build(MvkPipeline.TerminalFromTransport(transport));
```

Помните: `MvkPipeline` перестраивает цепочку в конструкторе, поэтому middleware,
добавленные в builder **после** вызова `Build`, на уже собранный пайплайн не влияют.

### 4.2. `MvkPipeline`

```csharp
public sealed class MvkPipeline
{
    public MvkPipeline(
        InvokeModbusDelegate terminal,
        IEnumerable<IModbusMiddleware> middlewares);

    public static InvokeModbusDelegate TerminalFromTransport(IMvkTransport transport);

    public async Task<ModbusResponse> ExecuteAsync(ModbusRequestContext context);
}
```

| Член | Назначение |
|---|---|
| Конструктор | Принимает терминал и middleware в порядке выполнения. Внутри проходит список в обратном порядке и оборачивает терминал: первый middleware становится головой цепочки |
| `TerminalFromTransport` | Готовый терминал поверх транспорта: вызывает `transport.ExecuteAsync` и кладёт результат в `context.Response` |
| `ExecuteAsync` | Запускает `head(context, context.Aborted)` и возвращает `context.Response`. Если ответа нет — `MvkModbusException(Unknown)` |

Обратите внимание: `ExecuteAsync` не принимает токен — он берётся из `context.Aborted`.
Поэтому контекст создаётся уже с нужным токеном вызывающего кода.

### 4.3. `MvkPipelineFactory`

```csharp
public static IReadOnlyList<IModbusMiddleware> CreateDefaultMiddlewares(
    ILoggerFactory? loggerFactory = null)
```

Возвращает новый набор middleware в порядке по умолчанию:

```text
TimeoutMiddleware → LoggingMiddleware → MetricsMiddleware
    → ValidationMiddleware → RetryMiddleware → WriteProtectionMiddleware
```

Если `loggerFactory` не передан, используется `NullLoggerFactory` (логирование молчит).
`MvkDevicePool` вызывает этот метод, когда ему не передали свой список middleware. В DI
используется не фабрика, а `MvkMiddlewareResolver`, который собирает тот же набор по именам
из конфигурации (см. раздел 8), — порядок и состав совпадают.

---

## 5. Встроенные middleware

Ниже — по разделу на каждый встроенный блок. Имена в конфигурации задаются константами
`MvkMiddlewareNames`: `timeout`, `logging`, `validation`, `retry`, `metrics`,
`writeProtection`.

### 5.1. `timeout` — `TimeoutMiddleware`

**Назначение.** Ограничивает **общее** время операции, включая повторы, задержки между
ними и подключение.

**Поведение.** Middleware прозрачен, если в `context.Items` нет ключа
`MvkContextItems.TimeoutMs` или значение `<= 0`. Иначе он создаёт связанный токен
(`CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)` + `CancelAfter`)
и передаёт его в `next`. Если по этому токену случилась отмена, а внешний токен **не**
отменён, исключение `OperationCanceledException` перехватывается и превращается в
ответ с ошибкой:

```csharp
context.Response = ModbusResponse.Failure(new MvkModbusException(
    $"Таймаут операции {context.Request.Operation} (0x{context.Request.StartAddress:X4}) — превышено {timeoutMs} мс.",
    MvkErrorKind.Timeout,
    context.Settings.Id));
```

Если же отменён внешний токен (`cancellationToken.IsCancellationRequested`), middleware
ничего не перехватывает — `OperationCanceledException` уходит вызывающему коду.

**Параметры.**

| Параметр | Где задаётся | По умолчанию |
|---|---|---|
| Бюджет операции | `Items[MvkContextItems.TimeoutMs]`, `IMvkDevice.ExecuteAsync(request, timeout, ct)` | не задан — middleware прозрачен |

**Пример.**

```csharp
// Команда SaveConfiguration (регистр 0x0002) может выполняться до 3 с — задаём бюджет операции.
ModbusResponse response = await device.ExecuteAsync(
    ModbusRequest.WriteMultipleRegisters(settings.Id, 0x0002, commandRegisters),
    TimeSpan.FromSeconds(3),
    cancellationToken);
```

**Взаимодействие.** Работает в паре с retry: при срабатывании таймаута посреди
`Task.Delay` повторной попытки `RetryMiddleware` получит `OperationCanceledException`,
а `TimeoutMiddleware` (внешний) превратит его в `Timeout`-ответ. Отдельно от сокетных
таймаутов: они живут в транспорте и не зависят от `TimeoutMs`.

### 5.2. `logging` — `LoggingMiddleware`

**Назначение.** Пишет в `ILogger<LoggingMiddleware>` одну строку на выполненную операцию:
устройство, операция, адрес, количество регистров, результат, длительность, номер попытки.

**Поведение.** Успех (`context.Response?.IsSuccess == true`) — уровень `Debug`. Всё
остальное, включая отсутствие ответа, — `Warning` (с категорией ошибки и сообщением).

**Параметры.**

| Параметр | Тип | По умолчанию |
|---|---|---|
| `logger` | `ILogger<LoggingMiddleware>?` | `NullLogger<LoggingMiddleware>.Instance` |

**Пример.**

```csharp
ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
MvkPipeline pipeline = new MvkPipelineBuilder()
    .Use(new LoggingMiddleware(loggerFactory.CreateLogger<LoggingMiddleware>()))
    .Build(MvkPipeline.TerminalFromTransport(transport));
```

**Взаимодействие.** В DI логгер подставляет `MvkMiddlewareResolver` из зарегистрированного
`ILoggerFactory`. Обратите внимание: успешные запросы логируются на уровне `Debug` — при
стандартном минимальном уровне `Information` их не видно. Включите `Debug` для категории
`Mvk.Modbus.Pipeline.Middlewares.LoggingMiddleware`, если нужны все запросы.

### 5.3. `metrics` — `MetricsMiddleware`

**Назначение.** Публикует метрики через `System.Diagnostics.Metrics`, совместимые с
OpenTelemetry, `dotnet-counters` и `MeterListener`.

**Поведение.** Инструменты статические, создаются один раз на процесс:

| Инструмент | Тип | Единица | Описание |
|---|---|---|---|
| `mvk.modbus.requests` | `Counter<long>` | — | Количество запросов |
| `mvk.modbus.duration` | `Histogram<double>` | мс | Длительность запросов |

Оба получают одинаковый набор тегов:

| Тег | Значение |
|---|---|
| `device` | `context.Settings.Id` |
| `operation` | `context.Request.Operation.ToString()` |
| `result` | `"success"` либо `"error"` (по итоговому `context.Response`) |

Meter: `new Meter("Mvk.Modbus", "1.0.0")`.

**Параметры.** Конструктор без параметров.

**Пример сбора через OpenTelemetry:**

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("Mvk.Modbus")
        .AddPrometheusExporter());
```

Быстрая проверка без приложения:

```powershell
dotnet-counters monitor --counters Mvk.Modbus -p <PID>
```

**Взаимодействие.** Так как инструменты статические, один и тот же счётчик получают все
пулы и устройства процесса. Метрика считается **на операцию**, а не на попытку:
`MetricsMiddleware` стоит снаружи `RetryMiddleware`, поэтому в тег `result` попадает
итоговый результат. Если поставить metrics внутрь retry, каждая попытка будет считаться
отдельным запросом.

### 5.4. `validation` — `ValidationMiddleware`

**Назначение.** Проверяет запрос до обращения к устройству: общие правила библиотеки и
особенности конкретного устройства.

**Поведение.** Вызывает статический метод `Validate(context)`; если тот вернул ошибку —
коротко замыкает пайплайн (`ModbusResponse.Failure`, `MvkErrorKind.Validation`). Политику
записи middleware **не** проверяет — это задача `writeProtection`.

```csharp
public static MvkModbusException? Validate(ModbusRequestContext context)
```

Что проверяется:

| Проверка | Ошибка |
|---|---|
| `request.IsWrite && request.WriteData is null` — защитная проверка (обычный конструктор `ModbusRequest` такое не пропустит) | `Validation` |
| `settings.RequireEvenAddressAndQuantity` и нечётный `StartAddress` или `Quantity` — особенность МВК01 | `Validation` |

Ограничения `Quantity` (1–125 для чтения, 1–123 для записи), диапазон адресов и наличие
данных записи контролирует сам `ModbusRequest` ещё в момент создания — до пайплайна.

**Параметры.** Конструктор без параметров; метод `Validate` статический, поэтому его можно
переиспользовать вне DI.

**Пример.**

```csharp
MvkModbusException? error = ValidationMiddleware.Validate(context);
if (error is not null)
{
    // запрос точно не пройдёт: адрес 0x2001 нечётен при RequireEvenAddressAndQuantity = true
}
```

**Взаимодействие.** В порядке по умолчанию стоит **до** retry: неверный запрос отклоняется
один раз, повторы на него не тратятся. Стоит **до** writeProtection: сначала проверяются
общие правила, потом политика записи.

### 5.5. `retry` — `RetryMiddleware`

**Назначение.** Повторяет запрос при временных ошибках, число попыток и задержку берёт из
параметров устройства.

**Алгоритм.** Число попыток: `maxAttempts = Math.Max(1, Settings.MaxRetries + 1)`.

```text
попытка = 1
цикл:
    context.Attempt = попытка
    context.Response = null
    await next(...)
    если ответ null или успешен      → выход
    если ошибка не IsTransient       → выход
    если попытка >= maxAttempts      → выход
    задержка = Settings.RetryDelayMs * попытка   (линейный рост)
    если задержка > 0 → await Task.Delay(задержка, cancellationToken)
    попытка++
```

Особенности:

- повторяются только ошибки с `IsTransient == true` — `Transport`, `Timeout`, `Busy`
  (см. `MvkModbusException.IsTransient`); ошибка Modbus от устройства (`DeviceError`) и
  любые `Validation` не повторяются;
- `context.Response` обнуляется в начале каждой попытки — повтор начинается «с чистого
  листа»;
- задержка растёт линейно: при `RetryDelayMs = 150` это 150 мс, 300 мс, 450 мс…;
- `Task.Delay` получает текущий токен, поэтому отмена (внешняя или по таймауту операции)
  немедленно прекращает цикл;
- новые попытки автоматически переподключаются: `NModbusTransport` при любой ошибке
  вызывает `CloseConnection()`, а следующий `ExecuteAsync` лениво подключается заново.
  При этом фоновая служба пула видит временную ошибку через `MvkDevice.ApplyResult` и
  планирует реконнект по экспоненциальной задержке.

**Параметры.**

| Параметр | Где задаётся | По умолчанию |
|---|---|---|
| `MaxRetries` | `Defaults` / устройство | 2 |
| `RetryDelayMs` | `Defaults` / устройство | 150 мс |

**Пример.**

```csharp
// MaxRetries = 2, RetryDelayMs = 150: до 3 обращений с паузами 150 и 300 мс.
MvkPipeline pipeline = new MvkPipelineBuilder()
    .Use(new RetryMiddleware())
    .Build(MvkPipeline.TerminalFromTransport(transport));
```

**Взаимодействие.** Внешний `TimeoutMiddleware` ограничивает суммарное время всех попыток.
Внутренний `WriteProtectionMiddleware` проверяет политику записи на каждой попытке.

### 5.6. `writeProtection` — `WriteProtectionMiddleware`

**Назначение.** Запрещает запись по политике устройства. Чтение не ограничивается никогда.

**Поведение.** Вызывает статический метод `Check(context)`; при отказе коротко замыкает
пайплайн (`ModbusResponse.Failure`, `MvkErrorKind.Validation`).

```csharp
public static MvkModbusException? Check(ModbusRequestContext context)
```

Три политики (`MvkWritePolicy`):

| Политика | Поведение |
|---|---|
| `AllowAll` | Запись разрешена во все регистры |
| `ProtectConfigArea` | Запись запрещена в защищённую область `ProtectedWriteStartAddress…ProtectedWriteEndAddress`, если `AllowConfigurationWrite == false`; остальные регистры доступны |
| `ReadOnly` | Запись запрещена полностью |

Пересечение диапазонов вычисляется по последнему затрагиваемому регистру:

```text
request.StartAddress <= settings.ProtectedWriteEndAddress
    && request.EndAddress >= settings.ProtectedWriteStartAddress
```

где `ModbusRequest.EndAddress => StartAddress + Quantity - 1`. То есть достаточно, чтобы
запрос задел защищённую область хотя бы одним регистром — отказ будет выдан.

**Параметры.**

| Параметр | Где задаётся | По умолчанию |
|---|---|---|
| `WritePolicy` | `Defaults` / устройство | `ProtectConfigArea` |
| `AllowConfigurationWrite` | `Defaults` / устройство | `false` |
| `ProtectedWriteStartAddress` / `ProtectedWriteEndAddress` | `Defaults` / устройство | `0x0000` / `0x0FFF` |

**Пример.**

```csharp
MvkModbusException? denial = WriteProtectionMiddleware.Check(context);
// denial != null, если запись в 0x0002 при ReadOnly или в защищённую область
```

Отличие от validation: проверка зависит от `Settings.WritePolicy`, а не от общего
устройства запроса, поэтому живёт в отдельном middleware. Для МВК01 запись
включается явно (`AllowConfigurationWrite = true` или
`ConnectAsync(..., allowConfigurationWrite: true)`) — иначе команды
`SaveConfiguration`/`Reboot` не пройдут защиту.

**Взаимодействие.** Стоит самым внутренним (перед терминалом) в порядке по умолчанию:
политика проверяется непосредственно перед обращением к сокету на каждой попытке.
Отказ имеет категорию `Validation` и `IsTransient == false`, поэтому retry не повторяет
запрещённую запись.

---

## 6. Правильный порядок и почему

Порядок по умолчанию задан дважды — в `MvkPipelineFactory.CreateDefaultMiddlewares`
и в `MvkModbusOptions.CreateDefaultPipeline()`:

```text
timeout → logging → metrics → validation → retry → writeProtection → транспорт
```

Смысл каждого решения:

| Решение | Почему так |
|---|---|
| `timeout` снаружи всего | Бюджет времени должен покрывать повторы, задержки и реконнект. Внутри retry таймаут ограничивал бы одну попытку, и общее время могло бы составить `(MaxRetries + 1) × timeout` |
| `logging`, `metrics` снаружи retry | Одна запись в лог и один замер на **операцию**, а не на попытку; в тег `result` попадает итог, в `Attempt` — номер последней попытки |
| `validation` до retry | Неверный запрос отклоняется мгновенно и не расходует попытки |
| `writeProtection` внутри retry (перед терминалом) | Политика проверяется прямо перед обращением к устройству, в том числе на повторных попытках |
| retry внутри timeout | Отмена по таймауту прерывает и цикл повторов, и ожидание между ними |

Что меняется при других порядках:

- **`retry` до `validation`.** Запрос проверяется на каждой попытке; при этом `Attempt`
  растёт даже на отклонённых запросах, а в лог/метрики может попасть несколько записей.
  Работает, но бессмысленно тратит проверки и путает диагностику. Опасный случай — если
  свой validation-middleware вернёт **временную** ошибку: retry начнёт её повторять.
- **`logging`/`metrics` внутри retry.** Каждая попытка логируется и считается отдельно.
  Иногда это нужно для отладки нестабильного канала, но по умолчанию засоряет метрики:
  три `Transport`-ошибки подряд дадут три записи `result=error` вместо одной.
- **`timeout` внутри retry.** Общий таймаут превращается в персональный таймаут попытки;
  операция может «висеть» дольше, чем ожидает вызывающий код.
- **`writeProtection` снаружи retry.** Формально запрет сработает так же (ответ не
  временный, повторов не будет), но проверка перестаёт быть «последней линией» перед
  сокетом. Держите её в конце — это дешевле, чем объяснять, почему запрос ушёл на
  устройство в обход политики.
- **`validation` после `writeProtection`.** Сначала отсекаются запросы по политике, потом
  по общему формату — результат для корректного кода тот же, но диагностика менее очевидна
  (пользователь получит отказ политики вместо ошибки формата).

Если меняете порядок — меняйте его осознанно и проверяйте тестами: `logging` и `metrics`
должны оставаться снаружи retry, `timeout` — снаружи всех, `writeProtection` — перед
терминалом.

---

## 7. Свой middleware

Пользовательский middleware — обычный класс, реализующий `IModbusMiddleware`. В DI он
создаётся через `ActivatorUtilities.CreateInstance`, поэтому в конструктор можно
запрашивать любые зарегистрированные службы: `ILogger<T>`, конфигурацию, свои сервисы.

### 7.1. `AuditMiddleware` — журнал операций

Полный пример: пишет в лог каждую операцию с результатом и длительностью.

```csharp
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Mvk.Modbus.Pipeline;

namespace MyApp;

/// <summary>
/// Аудит: журналирует каждый запрос к устройству с итогом и длительностью.
/// </summary>
public sealed class AuditMiddleware : IModbusMiddleware
{
    private readonly ILogger<AuditMiddleware> logger;

    public AuditMiddleware(ILogger<AuditMiddleware> logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(
        ModbusRequestContext context,
        InvokeModbusDelegate next,
        CancellationToken cancellationToken)
    {
        long startedAt = Stopwatch.GetTimestamp();

        await next(context, cancellationToken).ConfigureAwait(false);

        string result = context.Response?.IsSuccess == true
            ? "OK"
            : context.Response?.Error?.ErrorKind.ToString() ?? "нет ответа";

        logger.LogInformation(
            "Аудит МВК '{DeviceId}': {Operation} 0x{Address:X4} x{Quantity} — {Result}, "
            + "{ElapsedMs:F0} мс (попытка {Attempt}).",
            context.Settings.Id,
            context.Request.Operation,
            context.Request.StartAddress,
            context.Request.Quantity,
            result,
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
            context.Attempt);
    }
}
```

Регистрация и подключение:

```csharp
builder.Services.AddModbusMiddleware<AuditMiddleware>("audit");
```

```json
"Pipeline": ["timeout", "logging", "metrics", "audit", "validation", "retry", "writeProtection"]
```

Middleware размещён снаружи retry, поэтому в журнал попадает одна строка на операцию, а
`Attempt` показывает, сколько попыток в итоге потребовалось. Если нужно писать журнал и в
консоль, и в файл — просто настройте провайдеры логирования; отдельный код не нужен.

### 7.2. `TtlCacheMiddleware` — кэш результатов чтения

Задача: не дёргать устройство повторно за теми же регистрами, пока данные свежие.
Кэшируются **только чтения**, ключ — устройство + операция + адрес + число регистров,
срок жизни — TTL. Попадание в кэш коротко замыкает пайплайн.

```csharp
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Mvk.Modbus.Pipeline;
using Mvk.Modbus.Protocol;

namespace MyApp;

/// <summary>
/// Параметры кэша (регистрируются в DI, чтобы попасть в конструктор middleware).
/// </summary>
public sealed record TtlCacheOptions(TimeSpan Ttl);

/// <summary>
/// Кэширует успешные результаты чтения регистров на время TTL.
/// Записи не кэшируются: они обязаны доходить до устройства.
/// </summary>
public sealed class TtlCacheMiddleware : IModbusMiddleware
{
    private readonly TimeSpan ttl;
    private readonly ConcurrentDictionary<string, Entry> cache = new(StringComparer.Ordinal);

    public TtlCacheMiddleware(TtlCacheOptions options)
    {
        this.ttl = options.Ttl;
    }

    public Task InvokeAsync(
        ModbusRequestContext context,
        InvokeModbusDelegate next,
        CancellationToken cancellationToken)
    {
        string key = BuildKey(context);

        if (context.Request.IsWrite)
        {
            return WriteThroughAsync(context, next, cancellationToken, key);
        }

        if (cache.TryGetValue(key, out Entry? entry) && entry.ExpiresAt > DateTimeOffset.UtcNow)
        {
            context.Response = ModbusResponse.Success((ushort[])entry.Data.Clone());
            context.Items[MvkContextItems.ShortCircuited] = true;
            return Task.CompletedTask;
        }

        return ReadThroughAsync(context, next, cancellationToken, key);
    }

    private async Task ReadThroughAsync(
        ModbusRequestContext context,
        InvokeModbusDelegate next,
        CancellationToken cancellationToken,
        string key)
    {
        await next(context, cancellationToken).ConfigureAwait(false);

        ModbusResponse? response = context.Response;
        if (response is not null && response.IsSuccess)
        {
            cache[key] = new Entry(
                (ushort[])response.Data.Clone(),
                DateTimeOffset.UtcNow + ttl);
        }
    }

    private async Task WriteThroughAsync(
        ModbusRequestContext context,
        InvokeModbusDelegate next,
        CancellationToken cancellationToken,
        string key)
    {
        await next(context, cancellationToken).ConfigureAwait(false);

        // Успешная запись делает все закэшированные чтения этого устройства устаревшими.
        ModbusResponse? response = context.Response;
        if (response is not null && response.IsSuccess)
        {
            string prefix = context.Settings.Id + ":";
            foreach (string cachedKey in cache.Keys)
            {
                if (cachedKey.StartsWith(prefix, StringComparison.Ordinal))
                {
                    cache.TryRemove(cachedKey, out _);
                }
            }
        }
    }

    private static string BuildKey(ModbusRequestContext context)
    {
        return $"{context.Settings.Id}:{context.Request.Operation}"
            + $":0x{context.Request.StartAddress:X4}:x{context.Request.Quantity}";
    }

    private sealed record Entry(ushort[] Data, DateTimeOffset ExpiresAt);
}
```

Разбор решений:

- **Ключ включает операцию.** Регистры хранения (0x03) и входные регистры (0x04) —
  разные адресные пространства; без `Operation` в ключе чтение одного типа вернуло бы
  данные другого.
- **Копии массивов.** `ModbusResponse.Success` не копирует `ushort[]`, а `Data` доступен
  вызывающему коду. Клонирование на записи в кэш и на выдаче защищает кэш от случайной
  мутации.
- **Короткое замыкание.** При попадании выставляются `Response` и
  `Items[ShortCircuited] = true`, `next` не вызывается — ни валидация, ни retry, ни
  транспорт не работают.
- **Почему нельзя кэшировать записи.** Запись — это команда устройству, а не запрос
  данных: её результат зависит от текущего состояния железа, и «успех» из кэша означал бы,
  что команда не выполнена. Кроме того, запись меняет состояние, поэтому после неё
  необходимо инвалидировать кэш чтения (что и делает `WriteThroughAsync`).
- **TTL выбирает приложение.** Для быстро меняющихся измерений это десятки-сотни
  миллисекунд, для паспортных данных — минуты.

Регистрация и место в пайплайне:

```csharp
builder.Services.AddSingleton(new TtlCacheOptions(TimeSpan.FromMilliseconds(200)));
builder.Services.AddModbusMiddleware<TtlCacheMiddleware>("cache");
```

Middleware создаётся через `ActivatorUtilities.CreateInstance`, поэтому зарегистрированный
`TtlCacheOptions` попадёт в его конструктор. Для ручной сборки без DI используйте
конструктор напрямую: `new TtlCacheMiddleware(new TtlCacheOptions(TimeSpan.FromMilliseconds(200)))`.

```json
"Pipeline": ["timeout", "logging", "metrics", "validation", "cache", "retry", "writeProtection"]
```

Кэш обязан стоять **после** validation: иначе неверный запрос мог бы получить данные из
кэша, не пройдя проверку. Ставьте его перед retry — при промахе кэша повторные попытки
работают как обычно.

### 7.3. `RateLimitMiddleware` — ограничение частоты

Задача: не чаще одного запроса к устройству за интервал. Пример (упрощённый) хранит время
последнего запроса по каждому устройству:

```csharp
public sealed class RateLimitMiddleware : IModbusMiddleware
{
    private readonly TimeSpan minInterval;
    private readonly ConcurrentDictionary<string, DateTimeOffset> lastByDevice =
        new(StringComparer.OrdinalIgnoreCase);

    public RateLimitMiddleware(TimeSpan minInterval)
    {
        this.minInterval = minInterval;
    }

    public async Task InvokeAsync(
        ModbusRequestContext context,
        InvokeModbusDelegate next,
        CancellationToken cancellationToken)
    {
        DateTimeOffset previous = lastByDevice.GetOrAdd(context.Settings.Id, DateTimeOffset.MinValue);
        TimeSpan wait = previous + minInterval - DateTimeOffset.UtcNow;
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
        }

        lastByDevice[context.Settings.Id] = DateTimeOffset.UtcNow;
        await next(context, cancellationToken).ConfigureAwait(false);
    }
}
```

Замечания: при нескольких одновременных запросах к одному устройству проверка «последнего
времени» неточна — для строгого лимита нужен `SemaphoreSlim` на устройство; обычно
достаточно `MaxConcurrentRequests = 1`, который уже сериализует запросы. Размещать
ограничитель следует снаружи retry, чтобы пауза повторов тоже подчинялась лимиту.

---

## 8. Регистрация middleware в DI

### 8.1. `AddModbusMiddleware<T>`

```csharp
public static IServiceCollection AddModbusMiddleware<TMiddleware>(
    this IServiceCollection services,
    string? name = null)
    where TMiddleware : class, IModbusMiddleware
```

| Правило | Деталь |
|---|---|
| Имя по умолчанию | Если `name` не задан или пуст — берётся `typeof(TMiddleware).Name` (например, `AuditMiddleware`) |
| Пробелы | Имя обрезается (`name.Trim()`) |
| Регистр | Имена нечувствительны к регистру: `audit`, `Audit`, `AUDIT` — одно и то же |
| Повторная регистрация | Одно и то же имя перезаписывается: побеждает последний вызов |
| Неверный тип | Тип без `IModbusMiddleware` → `MvkModbusException(Configuration)` при регистрации |
| Экземпляры | Реестр хранит **типы**; экземпляр создаёт `MvkMiddlewareResolver` через `ActivatorUtilities.CreateInstance` при построении пула |

```csharp
services.AddLogging();
services.AddMvkModbus(configuration);
services.AddModbusMiddleware<AuditMiddleware>("audit");        // имя "audit"
services.AddModbusMiddleware<TtlCacheMiddleware>();            // имя "TtlCacheMiddleware"
```

### 8.2. Как разрешаются имена

`MvkMiddlewareResolver.ResolveMiddlewares(options, serviceProvider)` вызывается один раз —
при создании синглтона `MvkDevicePool`. Для каждого имени из `options.ResolvePipeline()`
по порядку:

1. проверяются встроенные имена (нечувствительно к регистру, с обрезкой пробелов):
   `timeout`, `logging`, `metrics`, `validation`, `retry`, `writeProtection`;
2. затем — реестр пользовательских типов `MvkMiddlewareRegistry`
   (`Dictionary<string, Type>` с `StringComparer.OrdinalIgnoreCase`);
3. если имени нет ни там, ни там — `MvkModbusException` с категорией `Configuration`:

```text
Middleware 'X' не найден: нет встроенного имени и нет регистрации
через AddModbusMiddleware<T>(services, "имя").
```

Обратите внимание: **встроенные имена имеют приоритет**. Зарегистрировать свой middleware
под именем `timeout` и «перекрыть» встроенный нельзя — выберите другое имя (`timeout2`,
`appTimeout`).

### 8.3. Порядок из конфигурации

Секция `Pipeline` — массив имён; порядок массива и есть порядок выполнения (первый —
самый внешний):

```json
{
  "MvkModbus": {
    "Pipeline": ["timeout", "logging", "metrics", "audit", "validation", "retry", "writeProtection"]
  }
}
```

| Значение `Pipeline` | Результат |
|---|---|
| `null` / отсутствует в JSON | Порядок по умолчанию: `timeout, logging, metrics, validation, retry, writeProtection` |
| Явный список | Строго в указанном порядке |
| `[]` (пустой массив) | Пайплайн без middleware: терминал вызывается напрямую |

### 8.4. Как отключить встроенный middleware

Просто уберите его имя из `Pipeline`:

```json
"Pipeline": ["timeout", "validation", "retry", "writeProtection"]
```

Здесь отключены `logging` и `metrics`. Программно:

```csharp
services.AddMvkModbus(options =>
{
    options.Pipeline = ["timeout", "validation", "retry", "writeProtection"];
    options.Devices = [new MvkDeviceOptions { Id = "mvk-1", Host = "10.20.0.211" }];
});
```

### 8.5. Как полностью отключить пайплайн

```csharp
options.Pipeline = [];   // пустой список — middleware нет вообще
```

`ResolvePipeline()` вернёт пустой список (важно: именно пустой список, а не `null` — иначе
сработал бы порядок по умолчанию). `MvkPipeline` получит пустой набор middleware и вызовет
терминал напрямую. При этом перестают работать **все** встроенные проверки, включая
`writeProtection` и `validation`. Для боевого устройства это опасно: запросы уйдут в сокет
как есть, а ошибки в адресах будут возвращаться устройством в виде Modbus-исключений.
Используйте только для отладки или полностью управляемого окружения.

---

## 9. Ручная сборка пайплайна без DI

Если контейнер не используется (консольная утилита, тест, сервис), пайплайн собирается
вручную. Полный пример с конфигурацией из JSON:

```csharp
using Microsoft.Extensions.Logging;
using Mvk.Modbus;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Pipeline;
using Mvk.Modbus.Pipeline.Middlewares;
using Mvk.Modbus.Protocol;
using Mvk.Modbus.Transport;

using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

MvkModbusOptions options = MvkModbusOptionsLoader.LoadFromJsonFile("mvkmodbus.json");
MvkDeviceSettings settings = options.ResolveDevices().First();

using NModbusTransport transport = new(settings);

MvkPipeline pipeline = new MvkPipelineBuilder()
    .Use(new TimeoutMiddleware())
    .Use(new LoggingMiddleware(loggerFactory.CreateLogger<LoggingMiddleware>()))
    .Use(new MetricsMiddleware())
    .Use(new ValidationMiddleware())
    .Use(new RetryMiddleware())
    .Use(new WriteProtectionMiddleware())
    .Build(MvkPipeline.TerminalFromTransport(transport));

using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));

ModbusRequestContext context = new(
    settings,
    ModbusRequest.ReadHoldingRegisters(settings.Id, 0x1004, 2),
    cts.Token);

ModbusResponse response = await pipeline.ExecuteAsync(context);

if (response.IsSuccess)
{
    Console.WriteLine($"Регистры: {string.Join(" ", response.Data.Select(v => $"0x{v:X4}"))}");
}
else
{
    Console.WriteLine($"Ошибка {response.Error!.ErrorKind}: {response.Error.Message}");
}
```

Короче — через готовую фабрику набора:

```csharp
MvkPipeline pipeline = new(
    MvkPipeline.TerminalFromTransport(transport),
    MvkPipelineFactory.CreateDefaultMiddlewares(loggerFactory));
```

Подводные камни ручной сборки:

1. **Новый контекст на каждый запрос.** Нельзя выполнить два запроса с одним
   `ModbusRequestContext`: в нём накапливаются `Response`, `Attempt` и `Items`.
   `MvkDevicePool` создаёт контекст сам — при ручной работе это ваша ответственность.
2. **Токен задаётся при создании контекста.** `ExecuteAsync` берёт `context.Aborted`;
   если создать контекст с `CancellationToken.None`, отменить операцию извне будет нельзя.
3. **Собственный транспорт — не потокобезопасен.** Один `NModbusTransport` и один
   мастер Modbus нельзя использовать из нескольких потоков одновременно. Если собираете
   пайплайн без пула, сериализуйте запросы сами (`SemaphoreSlim`) — либо используйте
   `MvkDevicePool`, где это уже сделано.
4. **Терминал — один на пайплайн.** Не передавайте в один `MvkPipeline` терминалы разных
   устройств: `context.Settings` и транспорт должны относиться к одному устройству.
5. **Middleware переиспользуемы, но потокобезопасность — на вас.** Один и тот же экземпляр
   можно передать в несколько пайплайнов (устройств), если он не хранит состояние запроса.
6. **Логирование.** `LoggingMiddleware` без логгера молчит; `NullLogger` — не ошибка.
7. **Уровень Debug.** Успешные запросы `LoggingMiddleware` пишет в `Debug` — настройте
   провайдер логирования, иначе строк не будет.

---

## 10. Отмена и таймауты

В работе участвуют три независимых механизма отмены/таймаута:

| Механизм | Где живёт | Как проявляется |
|---|---|---|
| Отмена вызывающим кодом | `context.Aborted`, `CancellationToken` в `ExecuteAsync` | `OperationCanceledException` наружу; ответ не формируется |
| Таймаут операции | `TimeoutMiddleware` + `Items[TimeoutMs]` | `ModbusResponse` с `MvkErrorKind.Timeout` |
| Сокетные таймауты | `NModbusTransport`: `ConnectTimeoutMs`, `ReadTimeoutMs`, `WriteTimeoutMs` | `ModbusResponse` с `MvkErrorKind.Timeout` из транспорта (прямой вызов `ConnectAsync` бросает `MvkModbusException(Timeout)`) |

Как они связаны:

- `MvkDevice.ExecuteCoreAsync` создаёт контекст с токеном вызывающего кода и (при
  перегрузке с `TimeSpan`) кладёт бюджет в `Items[TimeoutMs]`;
- `MvkPipeline.ExecuteAsync` передаёт `context.Aborted` в голову цепочки;
- `TimeoutMiddleware` создаёт **связанный** токен: отмена срабатывает либо по внешнему
  токену, либо по истечении `TimeoutMs`;
- терминал передаёт действующий токен в транспорт; транспорт бросает
  `OperationCanceledException`, если отменён именно переданный токен, и возвращает
  `Timeout`-ошибку, если сработал сокетный таймаут;
- `TimeoutMiddleware` перехватывает `OperationCanceledException` только когда внешний токен
  не отменён, и превращает его в `Timeout`-ответ. Если отменён внешний токен — исключение
  идёт наверх.

Сводка поведения:

| Ситуация | Что увидит вызывающий код |
|---|---|
| Отменён `ct` до/во время запроса | `OperationCanceledException` (в том числе из `Task.Delay` между попытками) |
| Истёк `TimeoutMs` | `ModbusResponse.Failure` с `MvkErrorKind.Timeout`; обёртки чтения/записи бросят `MvkModbusException(Timeout)` |
| Истёк `ReadTimeoutMs`/`WriteTimeoutMs`/`ConnectTimeoutMs` | Так же — `MvkErrorKind.Timeout` из транспорта |
| `TimeoutMs` не задан | Работают только сокетные таймауты, общий бюджет не ограничен |
| Устройство отключено (`Enabled = false`) | `MvkErrorKind.DeviceDisabled`, пайплайн вообще не выполняется |

Что делать с `OperationCanceledException`:

1. **Отличайте отмену от ошибки.** В `catch (OperationCanceledException)` проверяйте
   собственный токен (`ct.IsCancellationRequested`): если он отменён — это штатное
   завершение (остановка приложения, закрытие окна), а не сбой устройства. Не логируйте
   такое как `Warning`/`Error` и не пытайтесь повторять запрос.
2. **Не «проглатывайте» отмену.** Не возвращайте `null`/пустой массив: отмена должна
   дойти до вызывающего кода, иначе фоновые циклы не завершатся.
3. **Помните о неопределённости для записи.** Если отмена произошла после отправки
   запроса записи, но до получения ответа, состояние устройства неизвестно: команда могла
   выполниться. Для критичных операций (Save/Reboot) предусмотрите повторное чтение
   статуса после возобновления связи.
4. **Не создавайте токены без необходимости.** Для проверки «успел ли запрос уложиться в
   бюджет» достаточно `ExecuteAsync(request, timeout, ct)` — транспорт и таймаут-слой уже
   связаны.

Пример аккуратной обработки в фоновом опросе:

```csharp
try
{
    float[] values = await device.ReadMeasurementByNumberAsync(number, cts.Token);
    UpdateUi(values);
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
    // штатная остановка опроса
}
catch (MvkModbusException ex) when (ex.IsTransient)
{
    // связь пропала; реконнект выполняется пулом в фоне
}
catch (MvkModbusException ex)
{
    // ошибка данных или запроса — требуют внимания
}
```

---

## Подводные камни

1. **Один контекст — один запрос.** `Response`, `Attempt` и `Items` накапливаются в
   контексте; повторное использование экземпляра приведёт к ложному результату.
   Создавайте `ModbusRequestContext` заново на каждый запрос.
2. **Middleware общие для всех устройств пула.** Экземпляр один; любое поле-состояние
   должно быть потокобезопасным (`ConcurrentDictionary`, `Interlocked`), а лучше хранить
   состояние запроса в `context.Items`.
3. **Middleware без `next` обязан заполнить `Response`.** Иначе
   `MvkPipeline.ExecuteAsync` бросит `MvkModbusException(Unknown)`.
4. **`RetryMiddleware` обнуляет `Response` перед каждой попыткой.** Middleware, стоящий
   выше retry, не может заранее подготовить ответ в `context.Response` — его затрут.
5. **`TryGetItem<T>` строг по типу.** `long`, `TimeSpan` или строка в `Items[TimeoutMs]`
   не сработают: нужен именно `int`. Ключи `Items` регистрозависимы (`Ordinal`).
6. **Встроенные имена не перекрываются.** `AddModbusMiddleware<MyTimeout>("timeout")` не
   заменит встроенный `TimeoutMiddleware` — при разрешении встроенные проверяются первыми.
   Используйте другое имя.
7. **`Pipeline = null` и `Pipeline = []` — разные вещи.** Первое включает порядок по
   умолчанию, второе полностью отключает пайплайн (включая защиту записи и валидацию).
   Обратите внимание: в JSON пустой массив (`"Pipeline": []`) биндер превращает в
   `null` — то есть даёт порядок по умолчанию. Пустой пайплайн задаётся только
   программно.
8. **Порядок middleware — часть контракта.** `timeout` должен быть снаружи retry,
   `logging`/`metrics` — снаружи retry, `writeProtection` — перед терминалом. Перестановка
   меняет не только диагностику, но и поведение (см. раздел 6).
9. **`TimeoutMiddleware` не защищает от зависшего сокета сам по себе.** Без
   `Items[TimeoutMs]` он прозрачен; от «зависшего» чтения защищает `ReadTimeoutMs`
   транспорта.
10. **Успешные запросы логируются на уровне `Debug`.** При минимальном уровне
    `Information` в журнале будут видны только ошибки — это ожидаемое поведение, а не
    поломка логирования.
11. **Кэш чтения легко сделать неверным.** Ставьте его после validation, включайте в ключ
    операцию и устройство, клонируйте массивы и инвалидируйте записи устройства после
    успешной записи.
12. **Пайплайн и middleware фиксируются при создании пула.** Изменение `Pipeline` в
    конфигурации на лету не подхватится: синглтон `MvkDevicePool` строится один раз.
13. **DI-экземпляры middleware живут как singleton.** Пул — синглтон, `ActivatorUtilities`
    создаёт middleware один раз; внедрение scoped-службы в конструктор middleware
    превращает её в долгоживущую (captive dependency). Внедряйте singleton-службы или
    значения.
14. **Ручная сборка: транспорт не потокобезопасен.** Не разделяйте один `MvkPipeline` или
    `NModbusTransport` между потоками без собственной синхронизации — в пуле эту роль
    играет `requestGate` устройства.

---

## См. также

- [Пул устройств](05-device-pool.md) — как создаются устройства, их пайплайны и реконнект.
- [DI-глава](10-extensibility.md) — регистрация библиотеки, замена транспорта и сервисов.
- [README](../README.md) — краткая шпаргалка по пайплайну и полное описание API.
