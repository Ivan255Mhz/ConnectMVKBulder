# 10. Расширение библиотеки и интеграция с приложениями

Библиотека закрывает типовой сценарий «настроить пул МВК и читать значения», но у каждого
приложения есть свои требования: транспорт до устройства идёт через шлюз или VPN, нужен кэш
и аудит запросов, конфигурация проверяется по внутренним правилам, а значения должны
периодически попадать в лог, кэш, UI или HTTP-ответ. Для всего этого в библиотеке
предусмотрены точки расширения, не требующие правки её исходников.

Глава описывает:

- полный список точек расширения и границы, которые менять не следует;
- собственный транспорт (`IMvkTransport`) и его фабрику — с рабочим примером поверх TCP;
- транспорт-обёртку с задержкой для эмуляции медленного канала;
- свой middleware с DI-зависимостями и правила его подключения;
- собственный валидатор `IValidateOptions<MvkModbusOptions>`;
- фоновую службу приложения: периодический опрос, кэш, корректная остановка;
- интеграцию с WPF и ASP.NET Core;
- несколько пулов с разными настройками в одном процессе;
- ограничения расширяемости.

Все примеры используют те же типы и сигнатуры, что и исходный код библиотеки
(`src/Mvk.Modbus`, `src/Mvk.MVK01`); ссылки на главы руководства даны в конце.

---

## 1. Точки расширения

### 1.1. Что можно добавить или заменить

| Точка расширения | Контракт | Как включить | Что даёт |
|---|---|---|---|
| Транспорт | `IMvkTransport` + `IMvkTransportFactory` | своя регистрация `IMvkTransportFactory` после `AddMvkModbus` либо параметр конструктора `MvkDevicePool` | свой канал связи: шлюз, прокси, последовательный порт, эмулятор |
| Middleware | `IModbusMiddleware` | `AddModbusMiddleware<TMiddleware>("имя")` + имя в секции `Pipeline` | кэш, аудит, лимиты, бизнес-проверки на каждом запросе |
| Проверки конфигурации | `IValidateOptions<MvkModbusOptions>` | `AddSingleton<IValidateOptions<MvkModbusOptions>, TValidator>()` | собственные правила (префикс `Id`, обязательные теги, лимиты) |
| Фоновая служба | `IHostedService` / `BackgroundService` | `AddHostedService<TService>()` | периодический опрос, прогрев, обновление кэша, публикация наружу |
| Фабрика/обёртка МВК01 | `IMvk01DeviceFactory` | своя регистрация после `AddMvk01Devices` | другая кодировка строк, декорирование вызовов |
| Обёртка пула | `IMvkDevicePool` | регистрация декоратора после `AddMvkModbus` | сквозное логирование, телеметрия доступа к пулу |
| Доменная телеметрия | middleware + `System.Diagnostics.Metrics` | свой middleware | свои метрики поверх встроенной `metrics` |

Регистрация любой своей реализации выполняется по общему правилу .NET DI: если служба уже
зарегистрирована через `TryAdd*` (а `AddMvkModbus` использует именно `TryAddSingleton`),
то добавленная **позже** регистрация становится последней, а `GetRequiredService` возвращает
именно её. Важно только успеть до создания синглтона пула (то есть до старта хоста или
первого обращения к `IMvkDevicePool`).

### 1.2. Что менять нельзя

| Ограничение | Причина | Обходной путь |
|---|---|---|
| Перекрыть встроенное имя middleware (`timeout`, `logging`, `metrics`, `validation`, `retry`, `writeProtection`) | при разрешении пайплайна встроенные имена проверяются первыми | своё имя (`appTimeout`, `audit`) |
| Задать отдельный пайплайн для устройства | набор middleware один на пул, а не на устройство | разветвление внутри middleware по `context.Settings.Id` или второй пул (раздел 8) |
| Изменить или подменить запрос внутри middleware | `ModbusRequest` иммутабелен, `context.Request` — только get | сформировать новый `ModbusRequest` и выполнить его через `IMvkDevice` (помня о рекурсии) |
| Отключить или заменить `MvkModbusOptionsValidator` | валидатор регистрируется через `TryAddEnumerable` | дополнить своим валидатором (раздел 4) |
| Изменить публичные контракты и архитектуру библиотеки | обратная совместимость (см. AGENTS.md) | расширение без правки библиотеки: транспорт, middleware, валидаторы, hosted-сервисы |
| Использовать типы NModbus за пределами `Mvk.Modbus` | правило проекта: NModbus изолирован внутри сборки | работать только с `IMvkTransport`/`IMvkTransportFactory` |

---

## 2. Свой транспорт

### 2.1. Контракт `IMvkTransport`

Транспорт — единственное место, где запрос покидает библиотеку. Полный контракт
(пространство имён `Mvk.Modbus.Transport`):

```csharp
public interface IMvkTransport : IDisposable
{
    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken cancellationToken);

    Task DisconnectAsync();

    Task<ModbusResponse> ExecuteAsync(ModbusRequest request, CancellationToken cancellationToken);
}
```

Фабрика создаёт отдельный транспорт на каждое устройство пула:

```csharp
public interface IMvkTransportFactory
{
    IMvkTransport Create(MvkDeviceSettings settings);
}
```

Правила реализации, на которые опирается пул:

| Член | Требование |
|---|---|
| `IsConnected` | должен отражать реальное состояние: по нему `MvkDevice` принимает решения об idle-отключении, а `IMvkDevice.IsConnected` отдаётся вызывающему коду |
| `ConnectAsync` | таймаут подключения — `settings.ConnectTimeoutMs`; ошибки подключения — `MvkModbusException` (`Transport`/`Timeout`); отмена **внешнего** токена — `OperationCanceledException` |
| `DisconnectAsync` | закрывает соединение; идемпотентен; токена отмены в контракте нет — держите метод быстрым |
| `ExecuteAsync` | при необходимости подключается сам; транспортные ошибки возвращает как `ModbusResponse.Failure(...)`, а не исключением; `OperationCanceledException` при отмене переданного токена обязан уйти наружу |
| `Dispose` | идемпотентен; освобождает сокет, поток, семафоры |
| Потокобезопасность | при `MaxConcurrentRequests = 1` пул сериализует запросы сам, но `EnsureConnectedAsync` и фоновый `MaintainAsync` могут выполняться параллельно с обменом. Если `MaxConcurrentRequests > 1`, `ExecuteAsync` вызывается конкурентно — синхронизируйтесь (например, `SemaphoreSlim`) |

Заготовка возврата ответов: `ModbusResponse.Success(ushort[])` для чтения,
`ModbusResponse.WriteSuccess()` для записи, `ModbusResponse.Failure(MvkModbusException)`
для ошибки. Неизвестная операция — `Failure` с `MvkErrorKind.Validation`.

### 2.2. Полный пример: транспорт поверх сырого TCP

Ниже — законченный транспорт, который сам формирует Modbus/TCP-кадры (MBAP + PDU) и
разбирает ответ. Такой вариант пригодится, когда устройство доступно через нестандартный
шлюз, а полноценный `NModbusTransport` применить нельзя.

```csharp
using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mvk.Modbus;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Protocol;
using Mvk.Modbus.Transport;

namespace MyApp.Transport;

/// <summary>
/// Минимальный Modbus/TCP-транспорт поверх TcpClient: собирает и разбирает
/// кадры самостоятельно, без NModbus.
/// </summary>
public sealed class RawTcpTransport : IMvkTransport
{
    private readonly MvkDeviceSettings settings;
    private readonly ILogger<RawTcpTransport> logger;
    private readonly SemaphoreSlim connectLock = new(1, 1);
    private readonly SemaphoreSlim exchangeLock = new(1, 1);

    private TcpClient? client;
    private NetworkStream? stream;
    private int transactionId;
    private bool disposed;

    public RawTcpTransport(MvkDeviceSettings settings, ILogger<RawTcpTransport>? logger = null)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.logger = logger ?? NullLogger<RawTcpTransport>.Instance;
    }

    /// <inheritdoc/>
    public bool IsConnected => client is { Connected: true } && stream is not null;

    /// <inheritdoc/>
    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        await connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsConnected)
            {
                return;
            }

            Close();

            TcpClient tcp = new();
            using CancellationTokenSource timeoutSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(settings.ConnectTimeoutMs);

            try
            {
                await tcp.ConnectAsync(settings.Host, settings.Port, timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                tcp.Dispose();
                throw new MvkModbusException(
                    $"Таймаут подключения к {settings.Endpoint} ({settings.ConnectTimeoutMs} мс).",
                    MvkErrorKind.Timeout,
                    settings.Id);
            }
            catch (OperationCanceledException)
            {
                tcp.Dispose();
                throw;
            }
            catch (Exception ex) when (ex is not MvkModbusException)
            {
                tcp.Dispose();
                throw new MvkModbusException(
                    $"Не удалось подключиться к {settings.Endpoint}: {ex.Message}",
                    MvkErrorKind.Transport,
                    settings.Id,
                    null,
                    ex);
            }

            client = tcp;
            stream = tcp.GetStream();

            logger.LogDebug(
                "МВК '{DeviceId}': соединение установлено [{Endpoint}], SlaveId {SlaveId}.",
                settings.Id,
                settings.Endpoint,
                settings.SlaveId);
        }
        finally
        {
            connectLock.Release();
        }
    }

    /// <inheritdoc/>
    public Task DisconnectAsync()
    {
        Close();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<ModbusResponse> ExecuteAsync(ModbusRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        await exchangeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsConnected)
            {
                await ConnectAsync(cancellationToken).ConfigureAwait(false);
            }

            byte[] pdu = BuildPdu(request);
            ushort transaction = unchecked((ushort)Interlocked.Increment(ref transactionId));
            byte[] frame = BuildFrame(transaction, pdu);

            using CancellationTokenSource timeoutSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(request.IsWrite ? settings.WriteTimeoutMs : settings.ReadTimeoutMs);

            NetworkStream current = stream!;
            await current.WriteAsync(frame, timeoutSource.Token).ConfigureAwait(false);
            await current.FlushAsync(timeoutSource.Token).ConfigureAwait(false);

            byte[] header = new byte[7];
            await ReadExactAsync(current, header, timeoutSource.Token).ConfigureAwait(false);

            int length = (header[4] << 8) | header[5];
            if (length is < 2 or > 254)
            {
                Close();
                return ModbusResponse.Failure(new MvkModbusException(
                    $"Некорректный ответ {settings.Endpoint}: неверная длина кадра ({length}).",
                    MvkErrorKind.Transport,
                    settings.Id));
            }

            byte[] body = new byte[length - 1];
            await ReadExactAsync(current, body, timeoutSource.Token).ConfigureAwait(false);

            return ParseResponse(body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Close();
            throw;
        }
        catch (OperationCanceledException)
        {
            Close();
            return ModbusResponse.Failure(new MvkModbusException(
                $"Таймаут при обращении к {settings.Endpoint}.",
                MvkErrorKind.Timeout,
                settings.Id));
        }
        catch (MvkModbusException ex)
        {
            Close();
            return ModbusResponse.Failure(ex);
        }
        catch (Exception ex)
        {
            Close();
            return ModbusResponse.Failure(new MvkModbusException(
                $"Ошибка при обращении к {settings.Endpoint}: {ex.Message}",
                MvkErrorKind.Transport,
                settings.Id,
                null,
                ex));
        }
        finally
        {
            exchangeLock.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Close();
        connectLock.Dispose();
        exchangeLock.Dispose();
    }

    private static byte[] BuildPdu(ModbusRequest request)
    {
        return request.Operation switch
        {
            ModbusOperation.ReadHoldingRegisters => BuildReadPdu(0x03, request),
            ModbusOperation.ReadInputRegisters => BuildReadPdu(0x04, request),
            ModbusOperation.WriteSingleRegister => BuildWriteSinglePdu(request),
            ModbusOperation.WriteMultipleRegisters => BuildWriteMultiplePdu(request),
            _ => throw new MvkModbusException(
                $"Операция {request.Operation} не поддерживается транспортом.",
                MvkErrorKind.Validation,
                request.DeviceId)
        };
    }

    private static byte[] BuildReadPdu(byte function, ModbusRequest request)
    {
        return
        [
            function,
            (byte)(request.StartAddress >> 8),
            (byte)request.StartAddress,
            (byte)(request.Quantity >> 8),
            (byte)request.Quantity
        ];
    }

    private static byte[] BuildWriteSinglePdu(ModbusRequest request)
    {
        ushort value = request.WriteData![0];
        return
        [
            0x06,
            (byte)(request.StartAddress >> 8),
            (byte)request.StartAddress,
            (byte)(value >> 8),
            (byte)value
        ];
    }

    private static byte[] BuildWriteMultiplePdu(ModbusRequest request)
    {
        ushort[] data = request.WriteData!;
        byte[] pdu = new byte[6 + data.Length * 2];
        pdu[0] = 0x10;
        pdu[1] = (byte)(request.StartAddress >> 8);
        pdu[2] = (byte)request.StartAddress;
        pdu[3] = (byte)(data.Length >> 8);
        pdu[4] = (byte)data.Length;
        pdu[5] = (byte)(data.Length * 2);

        for (int i = 0; i < data.Length; i++)
        {
            pdu[6 + i * 2] = (byte)(data[i] >> 8);
            pdu[7 + i * 2] = (byte)data[i];
        }

        return pdu;
    }

    private byte[] BuildFrame(ushort transaction, byte[] pdu)
    {
        byte[] frame = new byte[7 + pdu.Length];
        frame[0] = (byte)(transaction >> 8);
        frame[1] = (byte)transaction;
        frame[2] = 0;
        frame[3] = 0;

        int length = pdu.Length + 1;   // SlaveId + PDU
        frame[4] = (byte)(length >> 8);
        frame[5] = (byte)length;
        frame[6] = settings.SlaveId;

        pdu.CopyTo(frame, 7);
        return frame;
    }

    private ModbusResponse ParseResponse(byte[] pdu)
    {
        // Исключение Modbus: старший бит кода функции установлен.
        if ((pdu[0] & 0x80) != 0)
        {
            byte code = pdu.Length > 1 ? pdu[1] : (byte)0;
            return ModbusResponse.Failure(new MvkModbusException(
                $"Устройство '{settings.Id}' вернуло ошибку Modbus 0x{code:X2} "
                + $"(функция 0x{pdu[0] & 0x7F:X2}).",
                MvkErrorKind.DeviceError,
                settings.Id,
                code));
        }

        if (pdu[0] is 0x06 or 0x10)
        {
            return ModbusResponse.WriteSuccess();
        }

        int byteCount = pdu[1];
        ushort[] registers = new ushort[byteCount / 2];
        for (int i = 0; i < registers.Length; i++)
        {
            registers[i] = (ushort)((pdu[2 + i * 2] << 8) | pdu[3 + i * 2]);
        }

        return ModbusResponse.Success(registers);
    }

    private static async Task ReadExactAsync(
        NetworkStream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream
                .ReadAsync(buffer.AsMemory(offset), cancellationToken)
                .ConfigureAwait(false);

            if (read == 0)
            {
                throw new IOException("Соединение закрыто удалённой стороной.");
            }

            offset += read;
        }
    }

    private void Close()
    {
        try
        {
            stream?.Dispose();
        }
        catch (Exception)
        {
        }

        try
        {
            client?.Close();
            client?.Dispose();
        }
        catch (Exception)
        {
        }

        stream = null;
        client = null;
    }
}
```

Разбор ключевых решений:

- **`exchangeLock`** сериализует обмен: даже при `MaxConcurrentRequests > 1` в сокет
  уходит один кадр за раз, а `transactionId` остаётся согласованным. Проверка
  `TransactionId` ответа не делается — при одном активном обмене она избыточна.
- **`connectLock`** защищает от параллельных `ConnectAsync` (запрос, `EnsureConnectedAsync`
  и фоновый реконнект могут стартовать одновременно).
- **Раздельные таймауты** `ConnectTimeoutMs` / `ReadTimeoutMs` / `WriteTimeoutMs` берутся
  из `MvkDeviceSettings` — те же поля, что использует `NModbusTransport`.
- **`OperationCanceledException` не превращается в ответ** — исключение уходит наружу,
  как того требует контракт; таймаут по внутреннему источнику, наоборот, становится
  `MvkErrorKind.Timeout`.
- **Любая транспортная ошибка закрывает соединение** — следующий запрос подключится
  заново, а `MvkDevice` переведёт устройство в `Faulted` и запланирует реконнект.

> Это учебный, но рабочий транспорт: он поддерживает четыре операции Modbus и рассчитан
> на устройство, отвечающее на каждый кадр ровно одним кадром. Для реального МВК01
> штатный `NModbusTransport` предпочтителен — он уже решает эти задачи.

### 2.3. Фабрика транспортов

```csharp
using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Transport;

namespace MyApp.Transport;

/// <summary>
/// Создаёт RawTcpTransport для каждого устройства пула.
/// </summary>
public sealed class RawTcpTransportFactory : IMvkTransportFactory
{
    private readonly ILoggerFactory loggerFactory;

    public RawTcpTransportFactory(ILoggerFactory? loggerFactory = null)
    {
        this.loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    }

    public IMvkTransport Create(MvkDeviceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new RawTcpTransport(settings, loggerFactory.CreateLogger<RawTcpTransport>());
    }
}
```

### 2.4. Регистрация: DI и вручную

**Через DI.** Порядок важен: `AddMvkModbus` регистрирует `NModbusTransportFactory` через
`TryAddSingleton`, поэтому своя регистрация должна идти **после** и будет последней — её и
получит пул.

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Mvk.Modbus.DependencyInjection;
using Mvk.Modbus.Transport;
using Mvk.MVK01.DependencyInjection;
using MyApp.Transport;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "mvkmodbus.json"), optional: false);

builder.Services.AddMvkModbus(builder.Configuration);
builder.Services.AddMvk01Devices();

// Последняя регистрация IMvkTransportFactory побеждает прежнюю (NModbusTransportFactory).
builder.Services.AddSingleton<IMvkTransportFactory>(provider =>
    new RawTcpTransportFactory(provider.GetRequiredService<ILoggerFactory>()));

using IHost host = builder.Build();
await host.StartAsync();
```

Что происходит под капотом:

1. `AddMvkModbus` добавляет дескриптор `IMvkTransportFactory → NModbusTransportFactory`;
2. `AddSingleton<IMvkTransportFactory>` добавляет второй дескриптор — он в коллекции позже;
3. `MvkDevicePool` (singleton) при создании вызывает
   `provider.GetRequiredService<IMvkTransportFactory>()` и получает последний дескриптор —
   вашу фабрику;
4. все устройства пула создаются уже с новым транспортом.

Обратная последовательность (`AddSingleton` до `AddMvkModbus`) тоже корректна: `TryAddSingleton`
увидит существующую регистрацию и не станет добавлять свою. Некорректна лишь регистрация
**после** того, как пул уже создан (например, после `host.StartAsync()`), — фабрика к тому
моменту уже вызвана для всех устройств.

**Вручную.** Конструктор `MvkDevicePool` принимает фабрику транспорта и список middleware:

```csharp
using Microsoft.Extensions.Logging;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Pipeline;
using Mvk.Modbus.Pipeline.Middlewares;
using Mvk.Modbus.Pool;
using MyApp.Transport;

using ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddConsole());

MvkModbusOptions options = MvkModbusOptionsLoader.LoadFromJsonFile("mvkmodbus.json");

IReadOnlyList<IModbusMiddleware> middlewares =
[
    new TimeoutMiddleware(),
    new LoggingMiddleware(loggerFactory.CreateLogger<LoggingMiddleware>()),
    new MetricsMiddleware(),
    new ValidationMiddleware(),
    new RetryMiddleware(),
    new WriteProtectionMiddleware()
];

await using MvkDevicePool pool = new(
    options,
    transportFactory: new RawTcpTransportFactory(loggerFactory),
    middlewares: middlewares,
    loggerFactory: loggerFactory);

IMvkDevice device = pool.GetDevice("mvk-1");
ushort[] registers = await device.ReadHoldingRegistersAsync(0x1004, 2);
```

Если `middlewares` не передавать, `MvkDevicePool` возьмёт набор
`MvkPipelineFactory.CreateDefaultMiddlewares(loggerFactory)` — полный аналог списка выше.

### 2.5. Транспорт-обёртка: эмуляция медленного канала

Обёртка вокруг штатного `NModbusTransport` добавляет задержку и логирование, не меняя
ни строчки в библиотеке. Удобно для проверки таймаутов, ретраев и UI-индикации «медленно».

```csharp
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Protocol;
using Mvk.Modbus.Transport;

namespace MyApp.Transport;

/// <summary>
/// Оборачивает штатный NModbusTransport, добавляя искусственную задержку канала.
/// Служит для эмуляции медленного соединения при разработке и тестах.
/// </summary>
public sealed class SlowChannelTransport : IMvkTransport
{
    private readonly NModbusTransport inner;
    private readonly int latencyMs;
    private readonly ILogger<SlowChannelTransport> logger;

    public SlowChannelTransport(MvkDeviceSettings settings, int latencyMs, ILoggerFactory loggerFactory)
    {
        inner = new NModbusTransport(settings, loggerFactory.CreateLogger<NModbusTransport>());
        this.latencyMs = Math.Max(0, latencyMs);
        logger = loggerFactory.CreateLogger<SlowChannelTransport>();
    }

    public bool IsConnected => inner.IsConnected;

    public Task ConnectAsync(CancellationToken cancellationToken) => inner.ConnectAsync(cancellationToken);

    public Task DisconnectAsync() => inner.DisconnectAsync();

    public async Task<ModbusResponse> ExecuteAsync(ModbusRequest request, CancellationToken cancellationToken)
    {
        if (latencyMs > 0)
        {
            await Task.Delay(latencyMs, cancellationToken).ConfigureAwait(false);
        }

        long startedAt = Stopwatch.GetTimestamp();
        ModbusResponse response = await inner.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);

        logger.LogDebug(
            "МВК '{DeviceId}' (медленный канал +{Latency} мс): {Operation} 0x{Address:X4} — {Result} за {Elapsed:F0} мс.",
            request.DeviceId,
            latencyMs,
            request.Operation,
            request.StartAddress,
            response.IsSuccess ? "OK" : response.Error?.ErrorKind.ToString(),
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

        return response;
    }

    public void Dispose() => inner.Dispose();
}
```

Фабрика-обёртка:

```csharp
public sealed class SlowChannelTransportFactory : IMvkTransportFactory
{
    private readonly int latencyMs;
    private readonly ILoggerFactory loggerFactory;

    public SlowChannelTransportFactory(int latencyMs, ILoggerFactory loggerFactory)
    {
        this.latencyMs = latencyMs;
        this.loggerFactory = loggerFactory;
    }

    public IMvkTransport Create(MvkDeviceSettings settings)
        => new SlowChannelTransport(settings, latencyMs, loggerFactory);
}
```

Подключение — как в разделе 2.4. Задержка добавляется **вокруг** сокетного обмена, поэтому
она не влияет на сокетные таймауты (`ReadTimeoutMs`), но увеличивает общее время операции:
учитывайте это при подборе `TimeoutMs` и `RetryDelayMs`. Если транспорт нужен только для
тестов без железа, используйте готовые `FakeModbusTransport` и `FakeModbusTransportFactory`
из `tests/Mvk.Testing` (см. [Рецепты](11-recipes.md)).

---

## 3. Свой middleware

### 3.1. Контракт

Напомним контракт (подробно — в главе [Пайплайн middleware](06-pipeline-middleware.md)):

```csharp
public delegate Task InvokeModbusDelegate(
    ModbusRequestContext context,
    CancellationToken cancellationToken);

public interface IModbusMiddleware
{
    Task InvokeAsync(
        ModbusRequestContext context,
        InvokeModbusDelegate next,
        CancellationToken cancellationToken);
}
```

Экземпляр middleware создаётся один раз на пул и используется всеми устройствами
одновременно, поэтому изменяемого состояния в полях быть не должно; состояние запроса
хранится в `context.Items`. Middleware может выполнить код до и после `next`, заменить
токен, коротко замкнуть цепочку, заполнив `context.Response`.

### 3.2. Пример с DI-зависимостями

Middleware создаётся через `ActivatorUtilities.CreateInstance`, поэтому в конструктор можно
запросить любые зарегистрированные singleton-службы.

```csharp
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Mvk.Modbus.Pipeline;

namespace MyApp.Middlewares;

/// <summary>
/// Счётчики, разделяемые middleware и приложением (регистрируются как singleton).
/// </summary>
public sealed class OperationCounters
{
    private long reads;
    private long writes;
    private long errors;

    public void OnRead() => Interlocked.Increment(ref reads);

    public void OnWrite() => Interlocked.Increment(ref writes);

    public void OnError() => Interlocked.Increment(ref errors);

    public long Reads => Interlocked.Read(ref reads);

    public long Writes => Interlocked.Read(ref writes);

    public long Errors => Interlocked.Read(ref errors);
}

/// <summary>
/// Считает операции чтения и записи, а также ответы с ошибкой.
/// </summary>
public sealed class CountingMiddleware : IModbusMiddleware
{
    private readonly OperationCounters counters;
    private readonly ILogger<CountingMiddleware> logger;

    public CountingMiddleware(OperationCounters counters, ILogger<CountingMiddleware> logger)
    {
        this.counters = counters;
        this.logger = logger;
    }

    public async Task InvokeAsync(
        ModbusRequestContext context,
        InvokeModbusDelegate next,
        CancellationToken cancellationToken)
    {
        if (context.Request.IsWrite)
        {
            counters.OnWrite();
        }
        else
        {
            counters.OnRead();
        }

        await next(context, cancellationToken).ConfigureAwait(false);

        if (context.Response?.IsSuccess == false)
        {
            counters.OnError();

            logger.LogWarning(
                "МВК '{DeviceId}': {Operation} 0x{Address:X4} завершился ошибкой {ErrorKind}.",
                context.Settings.Id,
                context.Request.Operation,
                context.Request.StartAddress,
                context.Response!.Error!.ErrorKind);
        }
    }
}
```

Регистрация и включение в пайплайн:

```csharp
builder.Services.AddSingleton<OperationCounters>();
builder.Services.AddModbusMiddleware<CountingMiddleware>("counters");
```

```json
{
  "MvkModbus": {
    "Pipeline": ["timeout", "logging", "metrics", "validation", "counters", "retry", "writeProtection"]
  }
}
```

Порядок включения — часть контракта:

| Имя | Класс | Роль |
|---|---|---|
| `timeout` | `TimeoutMiddleware` | общий бюджет операции, снаружи всех |
| `logging` | `LoggingMiddleware` | одна запись на операцию |
| `metrics` | `MetricsMiddleware` | метрики meter `Mvk.Modbus` на операцию |
| `validation` | `ValidationMiddleware` | проверка запроса до сети |
| `retry` | `RetryMiddleware` | повторы временных ошибок |
| `writeProtection` | `WriteProtectionMiddleware` | политика записи перед терминалом |

Пользовательский middleware размещайте внутри `timeout` и снаружи `retry`, если он должен
считать операции, а не попытки (как `counters` выше). Встроенные имена имеют приоритет:
зарегистрировать свой обработчик под именем `timeout` и тем самым «перекрыть» встроенный
нельзя — возьмите другое имя.

---

## 4. Свои проверки конфигурации

### 4.1. Валидатор `IValidateOptions<MvkModbusOptions>`

Дополнительные правила оформляются как обычный валидатор Options. Пример: у каждого
устройства пула `Id` обязан начинаться с `mvk-`.

```csharp
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using Mvk.Modbus.Configuration;

namespace MyApp.Configuration;

/// <summary>
/// Дополнительная проверка: идентификаторы устройств обязаны начинаться с "mvk-".
/// </summary>
public sealed class MvkIdPrefixValidator : IValidateOptions<MvkModbusOptions>
{
    private const string RequiredPrefix = "mvk-";

    public ValidateOptionsResult Validate(string? name, MvkModbusOptions options)
    {
        if (options.Devices is null || options.Devices.Count == 0)
        {
            return ValidateOptionsResult.Success;
        }

        List<string> failures = [];

        foreach (MvkDeviceOptions device in options.Devices)
        {
            string id = device.Id?.Trim() ?? string.Empty;

            if (!id.StartsWith(RequiredPrefix, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add(
                    $"Устройство '{id}': идентификатор Id должен начинаться с '{RequiredPrefix}' "
                    + "(правило приложения).");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
```

### 4.2. Регистрация и поведение

```csharp
builder.Services.AddMvkModbus(builder.Configuration);
builder.Services.AddMvk01Devices();

// Свой валидатор добавляется к встроенному MvkModbusOptionsValidator.
builder.Services.AddSingleton<IValidateOptions<MvkModbusOptions>, MvkIdPrefixValidator>();
```

Правила:

- `AddMvkModbus` регистрирует встроенный `MvkModbusOptionsValidator` через `TryAddEnumerable`
  и включает `ValidateOnStart()`. Ваш валидатор добавляется вторым и **не заменяет** встроенный:
  выполняются все;
- при старте хоста (или первом обращении к `IOptions<MvkModbusOptions>`) нарушения собираются
  в `OptionsValidationException`; свойство `Failures` содержит тексты от всех валидаторов;
- валидатор обязан возвращать `ValidateOptionsResult.Fail(...)`, а не бросать исключение
  из `Validate`;
- не завязывайтесь на порядок вызовов: список `options.Devices` может содержать `null`
  (встроенный валидатор тоже это проверяет);
- автономный загрузчик `MvkModbusOptionsLoader.LoadFromJsonFile` использует только встроенный
  валидатор и о ваших правилах не знает. Если конфигурация приходит из JSON без DI, вызовите
  свой валидатор вручную:

```csharp
ValidateOptionsResult result = new MvkIdPrefixValidator().Validate(null, options);
if (result.Failed)
{
    throw new MvkModbusException(
        $"Некорректная конфигурация: {string.Join(" ", result.Failures)}",
        MvkErrorKind.Configuration);
}
```

---

## 5. Фоновая служба приложения

### 5.1. Кэш измерений

Служба опроса обновляет разделяемый кэш, который читают UI или HTTP-обработчики. Класс
потокобезопасен и не зависит от библиотеки.

```csharp
using System;
using System.Collections.Concurrent;

namespace MyApp.Services;

/// <summary>
/// Последнее измеренное значение по устройству и каналу.
/// </summary>
public sealed record MeasurementSample(
    string DeviceId,
    int Channel,
    float Rms10To1000,
    DateTimeOffset Timestamp);

/// <summary>
/// Потокобезопасный кэш последних измерений.
/// </summary>
public sealed class MeasurementCache
{
    private readonly ConcurrentDictionary<string, MeasurementSample> samples =
        new(StringComparer.OrdinalIgnoreCase);

    public void Update(MeasurementSample sample)
        => samples[$"{sample.DeviceId}:{sample.Channel}"] = sample;

    public bool TryGet(string deviceId, int channel, out MeasurementSample? sample)
        => samples.TryGetValue($"{deviceId}:{channel}", out sample);

    public bool IsStale(string deviceId, int channel, TimeSpan maxAge, DateTimeOffset now)
        => TryGet(deviceId, channel, out MeasurementSample? sample)
            ? now - sample!.Timestamp > maxAge
            : true;
}
```

### 5.2. Служба периодического опроса

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mvk.Modbus;
using Mvk.MVK01;

namespace MyApp.Services;

/// <summary>
/// Каждые 500 мс читает СКЗ 10…1000 Гц по всем каналам и обновляет кэш приложения.
/// </summary>
public sealed class MeasurementPollingService : BackgroundService
{
    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(500);

    private readonly IMvk01DeviceFactory devices;
    private readonly MeasurementCache cache;
    private readonly ILogger<MeasurementPollingService> logger;

    public MeasurementPollingService(
        IMvk01DeviceFactory devices,
        MeasurementCache cache,
        ILogger<MeasurementPollingService> logger)
    {
        this.devices = devices;
        this.cache = cache;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Опрос МВК запущен: устройств {Count}.", devices.Devices.Count);

        using PeriodicTimer timer = new(Period);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                foreach (IMvk01Device device in devices.Devices)
                {
                    if (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

                    await PollDeviceAsync(device, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // штатная остановка хоста
        }

        logger.LogInformation("Опрос МВК остановлен.");
    }

    private async Task PollDeviceAsync(IMvk01Device device, CancellationToken cancellationToken)
    {
        try
        {
            float[] values = await device
                .ReadMeasurementByNumberAsync(Mvk01MeasurementNumber.Rms10To1000Acceleration, cancellationToken)
                .ConfigureAwait(false);

            DateTimeOffset now = DateTimeOffset.UtcNow;

            for (int channel = 1; channel <= values.Length; channel++)
            {
                cache.Update(new MeasurementSample(device.Id, channel, values[channel - 1], now));
            }

            logger.LogDebug(
                "МВК '{DeviceId}': СКЗ 10…1000 Гц (канал 1) = {Value:F4} м/с2.",
                device.Id,
                values[0]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // остановка — не ошибка
        }
        catch (MvkModbusException ex)
        {
            // Реконнект выполняет пул в фоне; опрос продолжается на следующем тике.
            logger.LogWarning(
                "МВК '{DeviceId}': опрос не удался ({ErrorKind}): {Message}",
                device.Id,
                ex.ErrorKind,
                ex.Message);
        }
    }
}
```

Регистрация:

```csharp
builder.Services.AddSingleton<MeasurementCache>();
builder.Services.AddHostedService<MeasurementPollingService>();
```

### 5.3. Остановка и связь с метриками

- `BackgroundService` останавливается хосте через `stoppingToken`: `PeriodicTimer` немедленно
  завершает ожидание, цикл выходит. Не глотайте `OperationCanceledException` от **своего**
  токена и не «продолжайте цикл» после остановки;
- не освобождайте пул и другие singleton-службы вручную: это делает DI-контейнер при
  `host.Dispose()`. Если нужно выполнить дополнительную очистку, переопределите `StopAsync`
  и после `await base.StopAsync(cancellationToken)` дождитесь своих задач;
- служба ходит в устройство через тот же пул и пайплайн, поэтому `MetricsMiddleware`
  считает её запросы наравне с остальными (meter `Mvk.Modbus`, инструменты
  `mvk.modbus.requests` и `mvk.modbus.duration`) — подробнее в главе
  [Ошибки, логирование и метрики](08-errors-logging-metrics.md);
- не запускайте несколько служб, опрашивающих одно и то же устройство: запросы
  сериализуются шлюзом `requestGate`, но суммарная нагрузка и задержки вырастут;
- 500 мс — безопасный период для МВК01 (см. [Рецепты](11-recipes.md)), но при нескольких
  устройствах лучше разнести старт опроса или увеличить период.

---

## 6. Интеграция с WPF

### 6.1. Композиция приложения

Пул регистрируется в DI как singleton, а окно и модель представления получают его через
контейнер. Хост приложения (generic host) удобно собрать в `App.xaml.cs`.

```csharp
using System;
using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Mvk.Modbus.DependencyInjection;
using Mvk.MVK01.DependencyInjection;
using MyApp.ViewModels;

namespace MyApp;

public partial class App : Application
{
    private IHost? host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        builder.Configuration.AddJsonFile(
            Path.Combine(AppContext.BaseDirectory, "mvkmodbus.json"),
            optional: false,
            reloadOnChange: false);

        builder.Services.AddMvkModbus(builder.Configuration);
        builder.Services.AddMvk01Devices();
        builder.Services.AddMvkModbusHostedService();
        builder.Services.AddSingleton<MainViewModel>();

        host = builder.Build();
        await host.StartAsync();

        MainViewModel viewModel = host.Services.GetRequiredService<MainViewModel>();

        MainWindow window = new() { DataContext = viewModel };
        window.Closed += async (_, _) =>
        {
            await viewModel.DisposeAsync();   // отписка от событий и остановка опроса
            await host.StopAsync();
            host.Dispose();
        };

        MainWindow = window;
        window.Show();

        await viewModel.StartPollingAsync();
    }
}
```

Пул создаётся лениво при первом обращении: `AddMvkModbusHostedService()` только логирует
состав, сами сокеты открываются при первом запросе.

### 6.2. `DeviceViewModel`

Минимальная рабочая модель строки списка с `INotifyPropertyChanged`.

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Mvk.Modbus.Pool;

namespace MyApp.ViewModels;

/// <summary>
/// Состояние и последнее значение одного МВК для привязок WPF.
/// </summary>
public sealed class DeviceViewModel : INotifyPropertyChanged
{
    private MvkDeviceState state;
    private string? lastError;
    private float value;

    public DeviceViewModel(string id, MvkDeviceState initialState)
    {
        Id = id;
        state = initialState;
    }

    public string Id { get; }

    public MvkDeviceState State
    {
        get => state;
        set
        {
            if (state == value)
            {
                return;
            }

            state = value;
            OnPropertyChanged();
        }
    }

    public string? LastError
    {
        get => lastError;
        set
        {
            if (lastError == value)
            {
                return;
            }

            lastError = value;
            OnPropertyChanged();
        }
    }

    public float Value
    {
        get => value;
        set
        {
            if (this.value.Equals(value))
            {
                return;
            }

            this.value = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
```

XAML-привязки — обычные: `ItemsSource="{Binding Devices}"`, `Text="{Binding Value, StringFormat=F4}"`,
индикатор состояния — `DataTrigger` по `State`.

### 6.3. `MainViewModel`: подписка на события и опрос

Событие `DeviceStateChanged` приходит из фонового потока пула, поэтому любые изменения
`ObservableCollection` и свойств моделей выполняются через `Dispatcher`.

```csharp
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Mvk.Modbus;
using Mvk.Modbus.Pool;
using Mvk.MVK01;

namespace MyApp.ViewModels;

/// <summary>
/// Список устройств, индикация состояния и периодическое чтение СКЗ.
/// </summary>
public sealed class MainViewModel : IAsyncDisposable
{
    private readonly IMvkDevicePool pool;
    private readonly IMvk01DeviceFactory devices;
    private readonly Dispatcher dispatcher;
    private readonly CancellationTokenSource cts = new();

    private Task? pollingTask;

    public MainViewModel(IMvkDevicePool pool, IMvk01DeviceFactory devices)
    {
        this.pool = pool;
        this.devices = devices;
        dispatcher = Application.Current.Dispatcher;

        foreach (IMvkDevice device in pool.Devices)
        {
            Devices.Add(new DeviceViewModel(device.Settings.Id, device.State));
        }

        pool.DeviceStateChanged += OnDeviceStateChanged;
    }

    public ObservableCollection<DeviceViewModel> Devices { get; } = [];

    public Task StartPollingAsync()
        => pollingTask ??= PollLoopAsync(cts.Token);

    private void OnDeviceStateChanged(object? sender, MvkDeviceStateChangedEventArgs args)
    {
        // Событие возникает в фоновом потоке пула — переносим изменения в UI-поток.
        _ = dispatcher.InvokeAsync(() =>
        {
            DeviceViewModel? vm = Find(args.Device.Settings.Id);
            if (vm is null)
            {
                return;
            }

            vm.State = args.NewState;
            vm.LastError = args.Error?.Message;
        });
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (IMvk01Device device in devices.Devices)
            {
                try
                {
                    float[] values = await device
                        .ReadMeasurementByNumberAsync(
                            Mvk01MeasurementNumber.Rms10To1000Acceleration, cancellationToken)
                        .ConfigureAwait(false);

                    DeviceViewModel? vm = Find(device.Id);
                    if (vm is not null)
                    {
                        await dispatcher.InvokeAsync(() => vm.Value = values[0]);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (MvkModbusException)
                {
                    // Состояние и последняя ошибка придут через DeviceStateChanged.
                }
            }
        }
    }

    private DeviceViewModel? Find(string id)
        => Devices.FirstOrDefault(item =>
            string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));

    public async ValueTask DisposeAsync()
    {
        pool.DeviceStateChanged -= OnDeviceStateChanged;

        await cts.CancelAsync().ConfigureAwait(false);

        if (pollingTask is not null)
        {
            try
            {
                await pollingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        cts.Dispose();
    }
}
```

На что обратить внимание:

- **Отписка от события.** Без `pool.DeviceStateChanged -= OnDeviceStateChanged` модель
  останется жить в пуле после закрытия окна (утечка);
- **`ObservableCollection` только в UI-потоке.** Добавление, удаление и замена элементов —
  через `Dispatcher`; иначе получите `NotSupportedException` при активной привязке;
- **Отмена при закрытии окна.** `DisposeAsync` отменяет токен, дожидается завершения цикла
  и только затем освобождает `CancellationTokenSource` (освобождать раньше — гонка с
  `PeriodicTimer`);
- **Не блокируйте UI.** Никаких `.Result`, `.Wait()` и `Thread.Sleep`; все обращения —
  `await` с `CancellationToken`;
- **Состояния.** `Connected` — зелёный индикатор, `Faulted` — красный и текст `LastError`,
  `Connecting`/`Disconnected` — нейтральный; состояние меняет сам пул, приложение лишь
  отображает.

---

## 7. Интеграция с ASP.NET Core

Регистрация в `Program.cs` минимального API:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Mvk.Modbus;
using Mvk.Modbus.DependencyInjection;
using Mvk.MVK01;
using Mvk.MVK01.DependencyInjection;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddMvkModbus(builder.Configuration);
builder.Services.AddMvk01Devices();
builder.Services.AddMvkModbusHostedService();

WebApplication app = builder.Build();

app.MapGet("/devices", (IMvk01DeviceFactory factory) =>
    factory.Devices.Select(device => new
    {
        id = device.Id,
        state = device.State.ToString()
    }));

app.MapGet("/devices/{id}/rms/{channel:int}", async (
    string id,
    int channel,
    IMvk01DeviceFactory factory,
    CancellationToken cancellationToken) =>
{
    if (channel is < 1 or > 8)
    {
        return Results.BadRequest(new { error = "Канал должен быть от 1 до 8." });
    }

    try
    {
        IMvk01Device device = factory.GetDevice(id);

        float[] values = await device.ReadMeasurementByNumberAsync(
            Mvk01MeasurementNumber.Rms10To1000Acceleration,
            cancellationToken);

        return Results.Ok(new
        {
            device = id,
            channel,
            rms = values[channel - 1],
            timestamp = DateTimeOffset.UtcNow
        });
    }
    catch (MvkModbusException ex) when (ex.IsTransient)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Устройство недоступно",
            detail: ex.Message);
    }
    catch (MvkModbusException ex)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status502BadGateway,
            title: "Ошибка устройства",
            detail: ex.Message);
    }
});

app.Run();
```

Особенности веб-сценария:

- `IMvkDevicePool` и `IMvk01DeviceFactory` — синглтоны, их можно безопасно внедрять в
  обработчики; пул создаётся один раз на процесс;
- запросы к одному устройству сериализуются (`MaxConcurrentRequests`, по умолчанию 1),
  поэтому при большом RPS очередь растёт: ограничивайте время операции
  (`device.ExecuteAsync(request, TimeSpan.FromSeconds(2), ct)`) и отдавайте 503 при
  таймауте, а не держите HTTP-запрос открытым;
- `CancellationToken` из минимального API — это токен отмены HTTP-запроса: если клиент
  отключился, чтение прервётся (`OperationCanceledException`), устройство может перейти
  в `Faulted`, а следующий запрос переподключится автоматически;
- **не блокируйте потоки** `.Result`/`.Wait()`/`GetAwaiter().GetResult()` — это ведёт к
  исчерпанию пула потоков и взаимным блокировкам;
- не читайте полный снимок канала на каждый HTTP-запрос: один вызов `ReadChannelAsync`
  — это пять Modbus-запросов. Для «горячих» endpoint'ов используйте кэш-middleware
  (пример `TtlCacheMiddleware` в главе [Пайплайн](06-pipeline-middleware.md)) или кэш
  приложения, обновляемый службой из раздела 5;
- `AddMvkModbusHostedService()` включает прогрев: ошибки конфигурации обнаружатся при
  старте приложения, а не при первом запросе клиента.

---

## 8. Несколько пулов с разными настройками

Один `AddMvkModbus` создаёт ровно один пул. Второй пул нужен, когда:

| Причина | Почему не хватит одного пула |
|---|---|
| Другой транспорт (шлюз, эмулятор, стенд) | фабрика транспорта задаётся на пул |
| Другой набор или порядок middleware | `Pipeline` и экземпляры middleware принадлежат пулу |
| Другие `Defaults` (таймауты, `WritePolicy`, `ByteOrder`) | `Defaults` общие для всех устройств пула |
| Независимое управление и освобождение | `Dispose` пула освобождает все его устройства |
| Изоляция сбоев и событий | свои `DeviceStateChanged`, свои реконнекты, свой прогрев |

Создавать второй пул нужно **вручную** — вторым вызовом `AddMvkModbus` этого не сделать:
оба вызова накопят конфигурацию в одних и тех же `IOptions<MvkModbusOptions>`, а
`TryAddSingleton` оставит первый зарегистрированный пул. Именованные регистрации в .NET 8
делаются через keyed-сервисы:

```csharp
using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mvk.Modbus;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Pool;
using Mvk.Modbus.Transport;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddKeyedSingleton<MvkDevicePool>(
    "line-1",
    (provider, _) => CreatePool(provider, "MvkModbusLine1"));

builder.Services.AddKeyedSingleton<MvkDevicePool>(
    "line-2",
    (provider, _) => CreatePool(provider, "MvkModbusLine2"));

static MvkDevicePool CreatePool(IServiceProvider provider, string sectionName)
{
    MvkModbusOptions options = MvkModbusOptionsLoader.LoadFromConfiguration(
        provider.GetRequiredService<IConfiguration>(), sectionName);

    ILoggerFactory loggerFactory = provider.GetRequiredService<ILoggerFactory>();

    // Для второго пула можно передать свою фабрику транспорта и свой набор middleware.
    return new MvkDevicePool(
        options,
        transportFactory: new NModbusTransportFactory(loggerFactory),
        loggerFactory: loggerFactory);
}
```

Использование именованных пулов:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Mvk.Modbus.Pool;

public sealed class Line1Poller
{
    private readonly MvkDevicePool pool;

    public Line1Poller([FromKeyedServices("line-1")] MvkDevicePool pool)
    {
        this.pool = pool;
    }

    public IMvkDevice GetDevice(string id) => pool.GetDevice(id);
}
```

Замечания:

- контейнер освобождает keyed-singleton'ы (`IDisposable`/`IAsyncDisposable`) сам при
  `DisposeAsync` провайдера — вручную `Dispose` не вызывайте;
- если второй пул создаётся в собственном реестре-фабрике, реализуйте в реестре
  `IAsyncDisposable` и освобождайте пулы в обратном порядке создания;
- фоновые службы для второго пула регистрируйте отдельно: `AddMvkModbusHostedService()`
  знает только про `IMvkDevicePool` «основного» пула;
- keyed-регистрация доступна в .NET 8 и в `HostApplicationBuilder`, и в `WebApplicationBuilder`.

---

## 9. Ограничения расширяемости

| Ограничение | Что это значит на практике |
|---|---|
| Нет per-device пайплайна | `MvkDevicePool` передаёт один и тот же список middleware всем устройствам; `MvkPipeline` создаётся на устройство, но экземпляры middleware общие. Разные наборы — только через отдельные пулы, иначе разветвляйте логику по `context.Settings.Id` |
| Нет приоритетов запросов | `requestGate` — обычный `SemaphoreSlim`, порядок близок к порядку поступления; приоритетных очередей, отмены «лишних» запросов и класса `MvkErrorKind.Busy` от пула нет (`Busy` зарезервирован под транспорт, который сам решает быть занятым) |
| Нет автоматического кэша | Кэширование — задача приложения: middleware с TTL (пример в главе [Пайплайн](06-pipeline-middleware.md)) или кэш, обновляемый фоновой службой (раздел 5) |
| `ModbusRequest` иммутабелен | Подменить или «поправить» запрос внутри middleware нельзя. Разрешённые варианты: отклонить запрос с `Validation` или выполнить собственный `ModbusRequest` через `IMvkDevice` (такой запрос снова пройдёт весь пайплайн — учитывайте рекурсию и защиту от неё) |
| Встроенные имена и валидатор не переопределяются | `AddModbusMiddleware<T>("timeout")` не заменит `TimeoutMiddleware`; `MvkModbusOptionsValidator` всегда выполняется. Расширение — только добавление |
| Пайплайн фиксируется при создании пула | `Pipeline` и `AddModbusMiddleware` читаются один раз при создании singleton-пула; горячее применение изменений конфигурации не поддерживается |
| Один терминал на пайплайн | Нельзя направить один `MvkPipeline` на разные устройства; транспорт и `context.Settings` должны относиться к одному устройству |
| NModbus изолирован | Прикладной код работает только с `IMvkTransport`/`IMvkTransportFactory`; типы NModbus не должны покидать сборку `Mvk.Modbus` |
| События синхронные | `StateChanged`/`DeviceStateChanged` вызываются в фоновом потоке пула, асинхронные обработчики не поддерживаются; переносите работу в `Dispatcher`/канал сами |
| Состояние — только в памяти | История измерений, БД, графики — ответственность приложения; библиотека хранит лишь текущее состояние соединения и настройки |

---

## Подводные камни

1. **Порядок регистрации транспорта.** Своя фабрика должна быть зарегистрирована после
   `AddMvkModbus` (или до него — тогда сработает `TryAdd`) и обязательно до создания пула.
   Регистрация после `host.StartAsync()`/первого `GetRequiredService<IMvkDevicePool>()`
   ни на что не повлияет: устройства уже созданы.
2. **`DisconnectAsync` без токена.** Контракт `IMvkTransport` не принимает
   `CancellationToken` в `DisconnectAsync` — не выполняйте там долгих операций;
   закрытие сокета должно быть быстрым и идемпотентным.
3. **Отмена ≠ ошибка.** Транспорт обязан пропускать `OperationCanceledException` наружу
   при отмене переданного токена. Если завернуть её в `ModbusResponse.Failure`, отмена
   перестанет работать, а фоновые циклы — завершаться.
4. **`IsConnected` влияет на поведение пула.** Завышенный или заниженный признак ломает
   idle-отключение, реконнект и `MvkDevice.IsConnected`; проверяйте реальное состояние
   (у `TcpClient.Connected` есть известные ограничения — при сомнениях переподключайтесь).
5. **Свой транспорт и `MaxConcurrentRequests`.** При значении больше 1 `ExecuteAsync`
   вызывается конкурентно; без `SemaphoreSlim` кадры перемешаются. В пуле с настройкой
   по умолчанию (1) запросы сериализованы, но `ConnectAsync` и `MaintainAsync` всё равно
   могут конкурировать.
6. **Задержка-обёртка и таймауты.** `Task.Delay` вокруг обмена увеличивает общее время
   операции; `ReadTimeoutMs` (сокет) при этом не сработает, а `TimeoutMs`/`RetryDelayMs`
   могут оказаться малы. Подбирайте значения согласованно.
7. **Middleware — общие и долгоживущие.** Экземпляр один на пул; поля должны быть
   потокобезопасными, а внедрение scoped-службы превращает её в captive dependency.
8. **Встроенные имена middleware не перекрываются.** Своя реализация под именем
   `timeout`/`retry` не будет использована — при разрешении имена проверяются первыми.
9. **Порядок middleware меняет смысл метрик.** Свой счётчик ставьте снаружи `retry`,
   если хотите считать операции, а не попытки; `timeout` держите снаружи всех.
10. **Свой валидатор не работает в автономном загрузчике.**
    `MvkModbusOptionsLoader` вызывает только встроенный `MvkModbusOptionsValidator`;
    при конфигурации без DI прогоняйте свои правила вручную.
11. **Валидатор не должен бросать исключения.** Возвращайте
    `ValidateOptionsResult.Fail(failures)`: только тогда `OptionsValidationException`
    соберёт все нарушения сразу.
12. **`BackgroundService`: не глотайте остановку.** Ловите `OperationCanceledException`
    от `stoppingToken`, не выполняйте после отмены новые запросы, не вызывайте
    `Task.Delay(..., CancellationToken.None)` в цикле.
13. **Не опрашивайте одно устройство из нескольких мест.** Пул сериализует запросы, но
    конкуренция циклов даёт очереди, лишний трафик и разброс периодов.
14. **WPF: событие приходит не в UI-поток.** Изменения `ObservableCollection` и свойств —
    только через `Dispatcher`; от событий обязательно отписывайтесь при закрытии.
15. **WPF: не освобождайте `CancellationTokenSource` раньше цикла.** Сначала отмена,
    затем ожидание задачи опроса, и лишь потом `Dispose` (см. `MainViewModel.DisposeAsync`).
16. **ASP.NET Core: не блокируйте запросы.** `.Result`/`.Wait()` в обработчике — путь к
    исчерпанию потоков; для тяжёлых чтений используйте фоновый кэш и отдавайте данные из него.
17. **Сеть — не БД.** Не выполняйте `ReadChannelAsync` (5 запросов) на каждый HTTP-запрос
    и не открывайте соединение заново на каждый вызов: пул уже управляет жизненным циклом.
18. **Не вызывайте `AddMvkModbus` дважды ради второго пула.** Конфигурации сольются в одних
    `IOptions`, а регистрация пула останется первой. Дополнительные пулы создавайте вручную
    (keyed-сервисы, раздел 8).
19. **Рекурсия в middleware.** Если ваш middleware сам выполняет запрос через
    `IMvkDevice`, этот запрос снова пройдёт через весь пайплайн, включая вас; без флага
    в `Items` получите бесконечный цикл.
20. **Запрос иммутабелен.** `context.Request` нельзя заменить; любые «правки на лету»
    запрещены архитектурно — это защищает валидацию и политику записи.

---

## См. также

- [Пайплайн middleware](06-pipeline-middleware.md) — контракты, встроенные обработчики,
  примеры кэша и аудита.
- [Пул устройств](05-device-pool.md) — состояния, реконнект, `AddDevice`/`RemoveDevice`,
  сериализация запросов.
- [Рецепты](11-recipes.md) — периодический опрос, тесты без железа, типовые сценарии.
- [README](../README.md) — быстрый старт, шпаргалка по API и конфигурации.
