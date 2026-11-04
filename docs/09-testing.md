# 09. Тестирование без железа

Эта глава — о том, как тестировать прикладной код поверх `ConnectMVKBulder`
и саму библиотеку без реального МВК01. Все инструменты лежат в проекте
`tests/Mvk.Testing`, а этот же проект используется автотестами самой библиотеки.

В главе разобраны:

- состав и назначение `Mvk.Testing`: `MvkTestServer`, `SilentTcpServer`,
  `FakeModbusTransport` / `FakeModbusTransportFactory`;
- когда какой инструмент выбирать;
- полные сигнатуры и примеры для каждого класса;
- как писать свои тесты: чтение через пул, конфигурация, пайплайн, ретраи,
  команды МВК01, таймауты;
- структура тестов самого репозитория и запуск `dotnet test` с фильтрами;
- приёмы, обеспечивающие стабильность тестов;
- ограничения эмулятора и ручной smoke-тест на реальном устройстве.

---

## 1. Обзор инструментов `Mvk.Testing`

`tests/Mvk.Testing` — библиотека тестовых двойников для Modbus/TCP. Она
позволяет проверить всю цепочку библиотеки (транспорт, пайплайн, пул, фасад
МВК01) без реального железа, а значит — в любой среде сборки и в CI.

Состав проекта:

| Файл | Классы | Назначение |
|---|---|---|
| `MvkTestServer.cs` | `MvkTestServer` | Modbus-slave на loopback-адресе с регистрами в памяти (на базе NModbus) |
| `SilentTcpServer.cs` | `SilentTcpServer` | TCP-сервер, принимающий соединения и не отвечающий; проверка таймаутов чтения |
| `FakeModbusTransport.cs` | `FakeModbusTransport`, `FakeModbusTransportFactory` | Программируемый транспорт: обработчик ответов, журнал запросов, управляемое состояние соединения |

Зависимости `Mvk.Testing.csproj`:

| Пакет / проект | Зачем |
|---|---|
| `NModbus` 3.0.81 | реализация Modbus-slave для `MvkTestServer` |
| `Microsoft.Extensions.Logging.Abstractions` 8.0.2 | типы логирования, нужные транзитно |
| `..\..\src\Mvk.Modbus\Mvk.Modbus.csproj` | интерфейсы `IMvkTransport`, `IMvkTransportFactory`, `MvkDeviceSettings` |

Это не NuGet-пакет: в свой тестовый проект добавьте ссылку на проект:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\tests\Mvk.Testing\Mvk.Testing.csproj" />
</ItemGroup>
```

Пространство имён всех типов — `Mvk.Testing`.

### Какой инструмент выбрать

| Что проверяете | Инструмент | Почему |
|---|---|---|
| Транспорт целиком: сокет, NModbus, кодеки регистров, реальный обмен | `MvkTestServer` | Настоящий Modbus/TCP-slave на `127.0.0.1` |
| Чтение/запись через пул и пайплайн | `MvkTestServer` | Ответы приходят по сети, состояние пула меняется как в бою |
| Таймаут чтения (устройство молчит) | `SilentTcpServer` | Соединение принимается, данные не отправляются |
| Пайплайн, ретраи, команды, ветвление логики | `FakeModbusTransport` | Ответы программируются, сокет не нужен, тест детерминирован |
| Учёт запросов (что именно отправлено) | `FakeModbusTransport` | Свойство `Requests` хранит журнал |
| Порты/адреса/кодеки без сети | `RegisterCodec`, `Mvk01Codec` напрямую | Чистые функции, юнит-тесты быстрее интеграционных |
| Чётность адресов, тайминги, реальные значения | Реальное устройство | Эмулятор этого не воспроизводит (см. раздел 8) |

Практическое правило: по умолчанию начинайте с `FakeModbusTransport` —
такой тест быстрый и не зависит от портов. `MvkTestServer` подключайте, когда
проверяете именно транспорт или хотите «настоящий» сквозной сценарий.
`SilentTcpServer` нужен только для таймаутов и разрыва связи.

---

## 2. `MvkTestServer`

Эмулятор Modbus-slave: слушает `127.0.0.1` на выбранном порту, хранит регистры
хранения и входные регистры в памяти, обслуживает запросы через NModbus.

```csharp
public sealed class MvkTestServer : IAsyncDisposable
```

Внутри: `TcpListener(IPAddress.Loopback, port)` → `ModbusFactory.CreateSlaveNetwork`
→ `CreateSlave(slaveId, dataStore)` на `SlaveDataStore` → `network.ListenAsync()`.
Сервер запускается в конструкторе, поэтому после `Start` можно сразу
подключаться.

### 2.1. API

| Член | Сигнатура | Описание |
|---|---|---|
| `Start` | `static MvkTestServer Start(byte slaveId = 1, int port = 0)` | Создаёт и запускает сервер. `port = 0` — свободный порт, выбранный ОС |
| `GetFreePort` | `static int GetFreePort()` | Возвращает свободный TCP-порт на loopback (сервер не создаётся) |
| `Port` | `int Port { get; }` | Фактический порт сервера; после `Start(port: 0)` берите его отсюда |
| `SlaveId` | `byte SlaveId { get; }` | Идентификатор Modbus (slave id), по умолчанию 1 |
| `SetHoldingRegister` | `void SetHoldingRegister(ushort address, ushort value)` | Записать один регистр хранения |
| `SetHoldingRegisters` | `void SetHoldingRegisters(ushort address, params ushort[] values)` | Записать несколько регистров подряд |
| `GetHoldingRegister` | `ushort GetHoldingRegister(ushort address)` | Прочитать один регистр хранения |
| `GetHoldingRegisters` | `ushort[] GetHoldingRegisters(ushort address, ushort count)` | Прочитать несколько регистров подряд |
| `DisposeAsync` | `ValueTask DisposeAsync()` | Остановить сервер и освободить сокет |

`MvkTestServer` реализует `IAsyncDisposable` — в тестах используйте
`await using`, тогда сервер гарантированно освободит порт после теста.
Метод `DisposeAsync` подавляет исключения остановки: ошибок от него ждать
не нужно.

### 2.2. Что эмулятор умеет

- Обслуживает функции Modbus через slave NModbus поверх `SlaveDataStore`:
  - `0x03` чтение регистров хранения — данные, которые вы задали
    через `SetHoldingRegister(s)`;
  - `0x04` чтение входных регистров — нули (API для их заполнения не
    предоставляется);
  - `0x06` запись одиночного регистра;
  - `0x10` запись нескольких регистров.
- Записи, выполненные клиентом, попадают в тот же store: их можно проверить
  вызовом `GetHoldingRegister(s)` после запроса.
- Поддерживает несколько последовательных подключений и переподключений
  (транспорт создаёт новый `TcpClient` на каждое соединение).
- Слушает только loopback: снаружи машины сервер недоступен.

### 2.3. Что эмулятор не умеет

- **Не проверяет чётность адреса и количества регистров.** Реальное МВК01
  отвергнет нечётный запрос ошибкой Modbus `0x01`, эмулятор его выполнит.
- **Не эмулирует 32-битную логику.** Порядок байт, `float`/`uint` и строки —
  забота клиента и кодеков; в store лежат просто `ushort`.
- **Не воспроизводит поведение устройства**: смену статуса системной команды,
  ошибки Flash, задержку Save до 3 с, сбойные состояния.
- **Не эмулирует таймауты и обрывы**: он всегда отвечает быстро; для таймаута
  нужен `SilentTcpServer`, для обрыва — остановка сервера (см. пример
  `ExecuteAsync_AfterServerRestart_Reconnects` в `NModbusTransportTests`).

Для всего перечисленного используйте `FakeModbusTransport` или само железо.

### 2.4. Пример: создание и чтение из теста

```csharp
using Mvk.Modbus;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Pool;
using Mvk.Testing;
using Xunit;

namespace MyApp.Tests;

public sealed class EmulatorSmokeTests
{
    [Fact]
    public async Task ReadHoldingRegisters_FromEmulator_ReturnsSeededData()
    {
        await using MvkTestServer server = MvkTestServer.Start();
        server.SetHoldingRegisters(0x1004, 0x0100, 0x4301);

        MvkModbusOptions options = new()
        {
            Defaults = new MvkDefaultsOptions
            {
                Port = server.Port,
                ConnectTimeoutMs = 2000,
                ReadTimeoutMs = 1000
            },
            Pool = new MvkPoolOptions { ReconnectEnabled = false },
            Devices = [new MvkDeviceOptions { Id = "mvk-1", Host = "127.0.0.1" }]
        };

        await using MvkDevicePool pool = new(options);
        IMvkDevice device = pool.GetDevice("mvk-1");

        ushort[] data = await device.ReadHoldingRegistersAsync(0x1004, 2);

        Assert.Equal(new ushort[] { 0x0100, 0x4301 }, data);
        Assert.Equal(MvkDeviceState.Connected, device.State);
    }
}
```

### 2.5. Пример: заполнение 32-битных значений

В store нет понятия «float» — значение раскладывается в регистры кодеком,
тем же, что использует библиотека:

```csharp
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Protocol;

// float по адресу 0x1004, порядок байт BigEndian
server.SetHoldingRegisters(0x1004, RegisterCodec.FromSingle(4.56f, MvkByteOrder.BigEndian));

// uint по адресу 0x1008
server.SetHoldingRegisters(0x1008, RegisterCodec.FromUInt32(161653, MvkByteOrder.BigEndian));
```

Проверка записи, выполненной клиентом:

```csharp
await device.WriteSingleRegisterAsync(0x0002, 0x0001);
Assert.Equal(0x0001, server.GetHoldingRegister(0x0002));

await device.WriteMultipleRegistersAsync(0x0100, [0x1111, 0x2222]);
Assert.Equal(new ushort[] { 0x1111, 0x2222 }, server.GetHoldingRegisters(0x0100, 2));
```

### 2.6. `GetFreePort` — когда нужен

`GetFreePort()` резервирует и сразу освобождает порт, чтобы получить число.
Он нужен для негативных сценариев, где сервер **не** должен слушать:

```csharp
int freePort = MvkTestServer.GetFreePort(); // никто не слушает — соединение отклоняется

MvkModbusOptions options = new()
{
    Defaults = new MvkDefaultsOptions { Port = freePort, ConnectTimeoutMs = 500, MaxRetries = 0 },
    Devices = [new MvkDeviceOptions { Id = "mvk-1", Host = "127.0.0.1" }]
};
```

Отдельно `GetFreePort` пригодится, когда сервер нужно остановить и поднять
заново **на том же порту** (проверка переподключения транспорта):

```csharp
int port = MvkTestServer.GetFreePort();
await using (MvkTestServer first = MvkTestServer.Start(port: port))
{
    // ... первый сервер работает
}

await using MvkTestServer second = MvkTestServer.Start(port: port);
second.SetHoldingRegister(0x0000, 0xABCD);
```

Между `GetFreePort()` и `Start(port)` есть гонка: порт теоретически может
занять другой процесс. Для изоляции тестов надёжнее `Start()` без аргумента
(`port = 0`) и `server.Port`.

---

## 3. `SilentTcpServer`

Сервер принимает TCP-соединение и молчит: ни одного байта ответа. Это
единственный корректный способ проверить именно **таймаут чтения** на уровне
транспорта.

```csharp
public sealed class SilentTcpServer : IAsyncDisposable
{
    public static SilentTcpServer Start(int port = 0);
    public int Port { get; }
    public ValueTask DisposeAsync();
}
```

| Член | Описание |
|---|---|
| `Start(int port = 0)` | Запускает сервер; `0` — свободный порт |
| `Port` | Фактический порт |
| `DisposeAsync()` | Закрывает listener и все принятые соединения |

Как это работает с точки зрения теста: клиент успешно подключается к серверу
(значит, ошибки `Transport` на подключении нет), отправляет запрос и ждёт
ответа до `ReadTimeoutMs`, после чего транспорт сообщает `MvkErrorKind.Timeout`.

### Пример: таймаут при чтении

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Mvk.Modbus;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Protocol;
using Mvk.Modbus.Transport;
using Mvk.Testing;
using Xunit;

namespace MyApp.Tests;

public sealed class TimeoutTests
{
    [Fact]
    public async Task Read_FromSilentServer_FailsWithTimeout()
    {
        await using SilentTcpServer silent = SilentTcpServer.Start();

        MvkDeviceSettings settings = new MvkDeviceOptions
        {
            Id = "mvk-1",
            Host = "127.0.0.1"
        }.Resolve(new MvkDefaultsOptions
        {
            Port = silent.Port,
            ReadTimeoutMs = 300,
            MaxRetries = 0
        });

        using NModbusTransport transport = new(settings);

        ModbusResponse response = await transport.ExecuteAsync(
            ModbusRequest.ReadHoldingRegisters("mvk-1", 0x0000, 2),
            CancellationToken.None);

        Assert.False(response.IsSuccess);
        Assert.Equal(MvkErrorKind.Timeout, response.Error!.ErrorKind);
        Assert.True(response.Error.IsTransient);
        Assert.False(transport.IsConnected);
    }
}
```

Замечания:

- `NModbusTransport` сам запросы не повторяет, поэтому в этом тесте
  `MaxRetries` ни на что не влияет. Но если тот же сценарий перенести в пул,
  без `MaxRetries = 0` повторы добавит `RetryMiddleware` — по умолчанию это
  три попытки с паузами.
- Тот же сценарий через пул даст `MvkModbusException` с
  `ErrorKind = MvkErrorKind.Timeout` (см. раздел 5.6).
- Если сервер вообще не слушает порт, ошибка будет другой
  (`Transport`/`Timeout` на подключении) — это проверяется через
  `GetFreePort`, а не через `SilentTcpServer`.

---

## 4. `FakeModbusTransport` и `FakeModbusTransportFactory`

Самый быстрый инструмент: транспорт вообще без сети. Ответы задаются функцией-
обработчиком, все отправленные запросы складываются в журнал.

```csharp
public sealed class FakeModbusTransport : IMvkTransport
{
    public FakeModbusTransport(Func<ModbusRequest, ModbusResponse>? handler = null);

    public List<ModbusRequest> Requests { get; }
    public bool IsConnected { get; private set; }

    public Task ConnectAsync(CancellationToken cancellationToken);
    public Task DisconnectAsync();
    public Task<ModbusResponse> ExecuteAsync(ModbusRequest request, CancellationToken cancellationToken);
    public void Dispose();
}
```

| Член | Описание |
|---|---|
| `FakeModbusTransport(handler)` | Конструктор с обработчиком. `handler = null` — поведение по умолчанию |
| `Requests` | Журнал всех запросов, дошедших до транспорта, в порядке выполнения |
| `IsConnected` | `true` после `ConnectAsync` и после успешного ответа; `false` после `DisconnectAsync`/`Dispose` |
| `ExecuteAsync` | Добавляет запрос в `Requests`, синхронно вызывает `handler`, возвращает его ответ |

Поведение по умолчанию (`handler` не задан):

- запись (`request.IsWrite == true`) → `ModbusResponse.WriteSuccess()`;
- чтение → `ModbusResponse.Success([0x0000, 0x0000])` (два нулевых регистра).

Фабрика транспортов — для пула:

```csharp
public sealed class FakeModbusTransportFactory : IMvkTransportFactory
{
    public FakeModbusTransportFactory(Func<MvkDeviceSettings, FakeModbusTransport>? factory = null);

    public List<FakeModbusTransport> Created { get; }
    public IMvkTransport Create(MvkDeviceSettings settings);
}
```

По умолчанию фабрика создаёт новый `FakeModbusTransport` на каждое устройство;
`Created` хранит все созданные экземпляры, чтобы их можно было проверить.

### 4.1. Пример: успешные ответы и ошибка

```csharp
// Ответы по умолчанию: запись — успех, чтение — [0x0000, 0x0000]
FakeModbusTransport defaults = new();

// Чтение конкретных данных
FakeModbusTransport seeded = new(_ => ModbusResponse.Success([0x0100, 0x4301]));

// Ошибка транспорта
FakeModbusTransport failing = new(_ => ModbusResponse.Failure(
    new MvkModbusException("устройство не отвечает", MvkErrorKind.Timeout, "mvk-1")));
```

Обратите внимание: `handler` синхронный, поэтому задержки, отмену и параллелизм
им не эмулировать — для этого нужен свой `IMvkTransport` (в автотестах
библиотеки для этого есть внутренний `FakeTransport` с параметром `delay`).

### 4.2. Пример: эмуляция командных регистров

Обработчик может хранить состояние устройства и реагировать на запись:

```csharp
ushort[] commandStatus = [0x0000, 0x0000];

FakeModbusTransport transport = new(request =>
{
    if (request.IsWrite && request.StartAddress == Mvk01RegisterMap.SystemCommand)
    {
        // Устройство «приняло» команду и выставило статус успеха
        commandStatus = RegisterCodec.FromUInt32(Mvk01Codec.StatusSuccess, MvkByteOrder.BigEndian);
        return ModbusResponse.WriteSuccess();
    }

    return ModbusResponse.Success(commandStatus);
});
```

Так проверяются и запись команды, и последующий опрос статуса — без
`SaveConfigurationAsync` на реальном железе. Фрагменту нужны пространства имён
`Mvk.Modbus.Protocol` (`RegisterCodec`, `ModbusResponse`), `Mvk.Modbus.Configuration`
(`MvkByteOrder`) и `Mvk.MVK01` (`Mvk01RegisterMap`, `Mvk01Codec`).

### 4.3. Пример: проверка отправленных запросов

```csharp
FakeModbusTransport transport = new();

MvkModbusOptions options = new()
{
    Defaults = new MvkDefaultsOptions { RequireEvenAddressAndQuantity = true },
    Pool = new MvkPoolOptions { ReconnectEnabled = false },
    Devices = [new MvkDeviceOptions { Id = "mvk-1", Host = "127.0.0.1" }]
};

await using MvkDevicePool pool = new(options, new FakeModbusTransportFactory(_ => transport), middlewares: []);
IMvkDevice device = pool.GetDevice("mvk-1");

await device.ReadHoldingRegistersAsync(0x2000, 2);

ModbusRequest request = Assert.Single(transport.Requests);
Assert.Equal("mvk-1", request.DeviceId);
Assert.Equal(ModbusOperation.ReadHoldingRegisters, request.Operation);
Assert.Equal((ushort)0x2000, request.StartAddress);
Assert.Equal((ushort)2, request.Quantity);
Assert.True(transport.IsConnected);
```

Передача `middlewares: []` в конструктор пула отключает встроенный пайплайн:
тест проверяет только маршрутизацию запроса до транспорта. В тестах самого
пайплайна middleware, наоборот, добавляются явно (разделы 5.3–5.5).

### 4.4. Фабрика на несколько устройств

```csharp
MvkModbusOptions options = new()
{
    Pool = new MvkPoolOptions { ReconnectEnabled = false },
    Devices =
    [
        new MvkDeviceOptions { Id = "mvk-1", Host = "127.0.0.1", Port = 1502 },
        new MvkDeviceOptions { Id = "mvk-2", Host = "127.0.0.1", Port = 1503 }
    ]
};

Dictionary<string, FakeModbusTransport> byId = new();

FakeModbusTransportFactory factory = new(settings =>
{
    FakeModbusTransport transport = new();
    byId[settings.Id] = transport;
    return transport;
});

await using MvkDevicePool pool = new(options, factory, middlewares: []);

IMvkDevice first = pool.GetDevice("mvk-1");
IMvkDevice second = pool.GetDevice("mvk-2");

// У каждого устройства — свой транспорт:
Assert.NotSame(byId["mvk-1"], byId["mvk-2"]);
```

---

## 5. Как тестировать свой код поверх библиотеки

Ниже — готовые xunit-примеры (xunit 2.5.3), которые можно переносить в свой
тестовый проект. Для интеграционных примеров нужна ссылка на `Mvk.Testing`;
юнит-примеры пайплайна и команд от неё не зависят.

### 5.1. Чтение через пул и эмулятор

```csharp
using System.Threading.Tasks;
using Mvk.Modbus;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Pool;
using Mvk.Testing;
using Xunit;

namespace MyApp.Tests;

public sealed class DevicePoolEmulatorTests
{
    [Fact]
    public async Task ReadHoldingRegisters_AgainstEmulator_ReturnsData()
    {
        await using MvkTestServer server = MvkTestServer.Start();
        server.SetHoldingRegisters(0x1004, 0x0100, 0x4301);

        MvkModbusOptions options = new()
        {
            Defaults = new MvkDefaultsOptions
            {
                Port = server.Port,
                ConnectTimeoutMs = 2000,
                ReadTimeoutMs = 1000,
                RequireEvenAddressAndQuantity = true
            },
            Pool = new MvkPoolOptions { ReconnectEnabled = false },
            Devices = [new MvkDeviceOptions { Id = "mvk-1", Host = "127.0.0.1" }]
        };

        await using MvkDevicePool pool = new(options);
        IMvkDevice device = pool.GetDevice("mvk-1");

        ushort[] data = await device.ReadHoldingRegistersAsync(0x1004, 2);

        Assert.Equal(new ushort[] { 0x0100, 0x4301 }, data);
        Assert.Equal(MvkDeviceState.Connected, device.State);
        Assert.Null(device.LastError);
    }
}
```

Тонкости:

- `ReconnectEnabled = false` отключает фоновый цикл обслуживания — тест не
  зависит от таймеров и не «дёргает» устройство после завершения.
- Порт берётся из `server.Port`: тест не конфликтует с другими тестами,
  которые выполняются параллельно.
- Если нужен типизированный слой, оберните устройство:
  `Mvk01Device mvk = new(pool.GetDevice("mvk-1"));` и вызывайте
  `ReadChannelAsync`, `ReadMeasurementByNumberAsync` и т.д.

### 5.2. Конфигурация: валидная и невалидная

Валидация — часть публичного контракта: ошибки настроек видны либо как
`OptionsValidationException` (DI), либо как `MvkModbusException`
(`MvkErrorKind.Configuration`) у `MvkModbusOptionsLoader`.

```csharp
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.DependencyInjection;
using Mvk.Modbus.Pool;
using Xunit;

namespace MyApp.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void ValidConfiguration_ResolvesPool()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddMvkModbus(options =>
        {
            options.Pool = new MvkPoolOptions { ReconnectEnabled = false };
            options.Devices = [new MvkDeviceOptions { Id = "mvk-1", Host = "127.0.0.1" }];
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        IMvkDevicePool pool = provider.GetRequiredService<IMvkDevicePool>();

        Assert.Single(pool.Devices);
        Assert.Equal("mvk-1", pool.Devices.Single().Settings.Id);
    }

    [Fact]
    public void InvalidConfiguration_ThrowsOptionsValidationException()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddMvkModbus(options =>
        {
            // Host не задан — валидатор обязан сообщить об этом
            options.Devices = [new MvkDeviceOptions { Id = "mvk-1" }];
        });

        using ServiceProvider provider = services.BuildServiceProvider();

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IMvkDevicePool>());

        Assert.Contains(exception.Failures, failure => failure.Contains("Host"));
    }
}
```

Если DI в тесте не нужен, валидатор можно вызвать напрямую:

```csharp
ValidateOptionsResult result = new MvkModbusOptionsValidator().Validate(null, options);
Assert.True(result.Failed);
Assert.Contains(result.Failures!, failure => failure.Contains("не уникален"));
```

### 5.3. Пайплайн со своим middleware: порядок и short-circuit

`MvkPipelineBuilder.Use` принимает как класс `IModbusMiddleware`, так и функцию
`Func<ModbusRequestContext, InvokeModbusDelegate, CancellationToken, Task>`.
Пример проверяет «луковичный» порядок обхода и короткое замыкание.

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mvk.Modbus;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Pipeline;
using Mvk.Modbus.Protocol;
using Xunit;

namespace MyApp.Tests;

public sealed class PipelineTests
{
    [Fact]
    public async Task Middlewares_RunInOrderAroundTerminal()
    {
        List<string> calls = [];

        MvkPipeline pipeline = new MvkPipelineBuilder()
            .Use(new TracingMiddleware("первый", calls))
            .Use(new TracingMiddleware("второй", calls))
            .Build((context, ct) =>
            {
                calls.Add("терминал");
                context.Response = ModbusResponse.Success([0x1234]);
                return Task.CompletedTask;
            });

        ModbusResponse response = await pipeline.ExecuteAsync(CreateContext());

        Assert.True(response.IsSuccess);
        Assert.Equal(
            new[] { "первый до next", "второй до next", "терминал", "второй после next", "первый после next" },
            calls);
    }

    [Fact]
    public async Task Middleware_ShortCircuit_SkipsTerminal()
    {
        bool terminalCalled = false;

        MvkPipeline pipeline = new MvkPipelineBuilder()
            .Use((context, next, ct) =>
            {
                context.Response = ModbusResponse.Failure(
                    new MvkModbusException("запись запрещена политикой", MvkErrorKind.Validation, context.Settings.Id));
                return Task.CompletedTask; // next не вызывается — цепочка оборвана
            })
            .Build((context, ct) =>
            {
                terminalCalled = true;
                context.Response = ModbusResponse.Success([]);
                return Task.CompletedTask;
            });

        ModbusResponse response = await pipeline.ExecuteAsync(CreateContext());

        Assert.False(response.IsSuccess);
        Assert.Equal(MvkErrorKind.Validation, response.Error!.ErrorKind);
        Assert.False(terminalCalled);
    }

    private static ModbusRequestContext CreateContext()
    {
        MvkDeviceSettings settings = new MvkDeviceOptions
        {
            Id = "mvk-1",
            Host = "127.0.0.1"
        }.Resolve(new MvkDefaultsOptions { MaxRetries = 0 });

        return new ModbusRequestContext(
            settings,
            ModbusRequest.ReadHoldingRegisters("mvk-1", 0x0000, 2),
            CancellationToken.None);
    }

    private sealed class TracingMiddleware : IModbusMiddleware
    {
        private readonly string name;
        private readonly List<string> calls;

        public TracingMiddleware(string name, List<string> calls)
        {
            this.name = name;
            this.calls = calls;
        }

        public async Task InvokeAsync(
            ModbusRequestContext context,
            InvokeModbusDelegate next,
            CancellationToken cancellationToken)
        {
            calls.Add($"{name} до next");
            await next(context, cancellationToken);
            calls.Add($"{name} после next");
        }
    }
}
```

Важно: если ни один middleware не вызовет `next` и не заполнит
`context.Response`, `MvkPipeline.ExecuteAsync` выбросит `MvkModbusException`
с `MvkErrorKind.Unknown`.

### 5.4. Ретраи с `FakeModbusTransport`

Проверяем, что временные ошибки повторяются, а число попыток соответствует
`MaxRetries`.

```csharp
using System.Threading;
using System.Threading.Tasks;
using Mvk.Modbus;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Pipeline;
using Mvk.Modbus.Pipeline.Middlewares;
using Mvk.Modbus.Protocol;
using Mvk.Testing;
using Xunit;

namespace MyApp.Tests;

public sealed class RetryTests
{
    [Fact]
    public async Task TransientFailures_AreRetriedUntilSuccess()
    {
        int attempts = 0;

        FakeModbusTransport transport = new(_ =>
        {
            attempts++;
            return attempts < 3
                ? ModbusResponse.Failure(
                    new MvkModbusException("нет связи", MvkErrorKind.Transport, "mvk-1"))
                : ModbusResponse.Success([0x1234]);
        });

        MvkDeviceSettings settings = new MvkDeviceOptions
        {
            Id = "mvk-1",
            Host = "127.0.0.1"
        }.Resolve(new MvkDefaultsOptions { MaxRetries = 2, RetryDelayMs = 1 });

        MvkPipeline pipeline = new MvkPipelineBuilder()
            .Use(new RetryMiddleware())
            .Build(MvkPipeline.TerminalFromTransport(transport));

        ModbusResponse response = await pipeline.ExecuteAsync(new ModbusRequestContext(
            settings,
            ModbusRequest.ReadHoldingRegisters("mvk-1", 0x0000, 2),
            CancellationToken.None));

        Assert.True(response.IsSuccess);
        Assert.Equal(3, attempts);                      // 1 попытка + 2 повтора
        Assert.Equal(3, transport.Requests.Count);      // столько же запросов ушло в транспорт
        Assert.All(transport.Requests, request => Assert.Equal("mvk-1", request.DeviceId));
    }
}
```

Если ошибка нетранзиентная (`IsTransient == false`), `RetryMiddleware`
не повторяет запрос — в журнале останется ровно один запрос. Это поведение
покрыто тестами `MiddlewareTests.Retry_NonTransientFailure_NotRetried`.

### 5.5. Команды МВК01: `SaveConfigurationAsync` со статусом 0

Полный сценарий команды: запись двух регистров по адресу `Mvk01RegisterMap.SystemCommand`
(0x0002), затем опрос статуса, пока устройство не вернёт `Mvk01Codec.StatusSuccess` (0).

```csharp
using System.Threading.Tasks;
using Mvk.Modbus;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Pool;
using Mvk.Modbus.Protocol;
using Mvk.MVK01;
using Mvk.Testing;
using Xunit;

namespace MyApp.Tests;

public sealed class CommandTests
{
    [Fact]
    public async Task SaveConfigurationAsync_StatusSuccess_ReturnsIsSuccess()
    {
        FakeModbusTransport transport = new(request => request.IsWrite
            ? ModbusResponse.WriteSuccess()
            : ModbusResponse.Success(
                RegisterCodec.FromUInt32(Mvk01Codec.StatusSuccess, MvkByteOrder.BigEndian)));

        MvkModbusOptions options = new()
        {
            Defaults = new MvkDefaultsOptions
            {
                RequireEvenAddressAndQuantity = true,
                AllowConfigurationWrite = true
            },
            Pool = new MvkPoolOptions { ReconnectEnabled = false },
            Devices = [new MvkDeviceOptions { Id = "mvk-1", Host = "127.0.0.1" }]
        };

        await using MvkDevicePool pool = new(
            options,
            new FakeModbusTransportFactory(_ => transport),
            middlewares: []);

        Mvk01Device device = new(pool.GetDevice("mvk-1"));

        Mvk01CommandResult result = await device.SaveConfigurationAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(Mvk01CommandStatus.Success, result.Status);
        Assert.Equal(0u, result.DeviceStatus);

        ModbusRequest write = Assert.Single(transport.Requests, request => request.IsWrite);
        Assert.Equal(Mvk01RegisterMap.SystemCommand, write.StartAddress);
        Assert.Equal(new ushort[] { 0x000F, 0x0001 }, write.WriteData);
    }
}
```

Что здесь проверяется:

- команда закодирована с учётом порядка байт (`0x000F 0x0001` для
  `SaveConfiguration` в BigEndian);
- статус `0` распознан как `Mvk01CommandStatus.Success`;
- журнал подтверждает адрес и данные записи.

С `middlewares: []` политика записи не участвует, поэтому флаг
`AllowConfigurationWrite` в этом тесте — декларация намерения. В DI-конфигурации
(и на реальном устройстве) он обязателен для команд, см.
[Конфигурация](03-configuration.md).

Аналогично тестируются ветки ошибок: `Mvk01Codec.StatusBadCommand` →
`Mvk01CommandStatus.BadCommand`, `Mvk01Codec.StatusFlashError` →
`Mvk01CommandStatus.FlashError`, «зависший» статус → `Timeout` при коротком
таймауте операции:

```csharp
Mvk01CommandResult result = await device.ExecuteSystemCommandAsync(
    Mvk01SystemCommand.SaveConfiguration,
    TimeSpan.FromMilliseconds(300));

Assert.Equal(Mvk01CommandStatus.Timeout, result.Status);
```

### 5.6. Таймаут молчащего сервера через пул

Тот же сценарий, что в разделе 3, но на уровне `IMvkDevice`: проверяем, что
ошибка доходит до вызывающего кода с нужным `ErrorKind`.

```csharp
using System.Threading.Tasks;
using Mvk.Modbus;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Pool;
using Mvk.Testing;
using Xunit;

namespace MyApp.Tests;

public sealed class DeviceTimeoutTests
{
    [Fact]
    public async Task Read_FromSilentServer_ThrowsTimeoutException()
    {
        await using SilentTcpServer silent = SilentTcpServer.Start();

        MvkModbusOptions options = new()
        {
            Defaults = new MvkDefaultsOptions
            {
                Port = silent.Port,
                ConnectTimeoutMs = 1000,
                ReadTimeoutMs = 300,
                MaxRetries = 0
            },
            Pool = new MvkPoolOptions { ReconnectEnabled = false },
            Devices = [new MvkDeviceOptions { Id = "mvk-1", Host = "127.0.0.1" }]
        };

        await using MvkDevicePool pool = new(options);
        IMvkDevice device = pool.GetDevice("mvk-1");

        MvkModbusException exception = await Assert.ThrowsAsync<MvkModbusException>(
            () => device.ReadHoldingRegistersAsync(0x1004, 2));

        Assert.Equal(MvkErrorKind.Timeout, exception.ErrorKind);
        Assert.True(exception.IsTransient);
        Assert.Equal(MvkDeviceState.Faulted, device.State);
    }
}
```

`ReadTimeoutMs = 300` держит тест быстрым; `MaxRetries = 0` гарантирует одну
попытку. Состояние `Faulted` — ожидаемая реакция пула на временную ошибку;
при `ReconnectEnabled = true` дальше включится фоновый реконнект.

---

## 6. Тесты в этом репозитории

Автотесты самой библиотеки используют `Mvk.Testing` так же, как это будет
делать ваш проект.

### 6.1. Структура

`tests/Mvk.Modbus.Tests` — 123 теста (95 `[Fact]` + 10 `[Theory]` c 28
вариантами `[InlineData]`):

| Папка / файл | Что покрыто |
|---|---|
| `Configuration/MvkDeviceOptionsTests.cs` | `Resolve`, наследование defaults, валидатор |
| `Configuration/MvkModbusOptionsLoaderTests.cs` | загрузка JSON, ошибки конфигурации |
| `DependencyInjection/MvkModbusServiceCollectionExtensionsTests.cs` | `AddMvkModbus`, биндинг секции, `OptionsValidationException`, свои middleware, hosted-сервис |
| `Pipeline/MvkPipelineTests.cs` | порядок middleware, short-circuit, проброс токена, ошибка «нет ответа» |
| `Pipeline/MiddlewareTests.cs` | все встроенные middleware: retry, timeout, validation, writeProtection, logging, metrics |
| `Pipeline/PipelineIntegrationTests.cs` | полный пайплайн против `MvkTestServer`, ретраи при недоступном сервере |
| `Pool/MvkDeviceTests.cs` | чтение/запись через эмулятор, сериализация запросов, состояния, фоновое обслуживание |
| `Pool/MvkDevicePoolTests.cs` | API пула: `GetDevice`, `AddDevice`, `RemoveDevice`, события, `MaintainAsync` |
| `Protocol/RegisterCodecTests.cs`, `ModbusRequestTests.cs`, `ModbusResponseTests.cs` | кодеки и модели протокола |
| `Transport/NModbusTransportTests.cs` | транспорт против `MvkTestServer` и `SilentTcpServer`, переподключение, `MapException` |
| `TestDeviceSettings.cs`, `FakeTransport.cs` | внутренние помощники тест-проекта: быстрые настройки и транспорт с задержкой/счётчиками |

`tests/Mvk.MVK01.Tests` — 79 тестов (45 `[Fact]` + 13 `[Theory]` c 34
вариантами `[InlineData]`):

| Файл | Что покрыто |
|---|---|
| `Mvk01RegisterMapTests.cs` | адреса каналов, тахометров, параметров по номерам |
| `Mvk01CodecTests.cs` | версия ПО, битовые поля, cp1251 и swap-строки, команды |
| `Mvk01DeviceTests.cs` | снимки канала/тахометра/конфигурации, параметры по номерам, команды |
| `Mvk01ConnectionTests.cs` | режим «только чтение» по умолчанию, opt-in запись, защищённая область, Save без разрешения |
| `Polling/Mvk01WatcherTests.cs` | периодический опрос, интервал, ошибки, отмена, валидация |
| `DependencyInjection/Mvk01ServiceCollectionExtensionsTests.cs` | `AddMvk01Devices`, фабрика, чётность; запись по умолчанию запрещена |

Пакеты тестов одинаковые в обоих проектах: `xunit` 2.5.3,
`Microsoft.NET.Test.Sdk` 17.8.0, `xunit.runner.visualstudio` 2.5.3,
`coverlet.collector` 6.0.0, плюс `Microsoft.Extensions.DependencyInjection`
и `Logging` для DI-тестов.

### 6.2. Запуск

```powershell
dotnet test                                        # всё решение
dotnet test tests/Mvk.Modbus.Tests                 # только Mvk.Modbus.Tests
dotnet test tests/Mvk.MVK01.Tests                  # только Mvk.MVK01.Tests
```

Фильтры по имени (xunit поддерживает выражения `FullyQualifiedName`,
`DisplayName`):

```powershell
dotnet test tests/Mvk.Modbus.Tests --filter "FullyQualifiedName~Retry"
dotnet test --filter "FullyQualifiedName~Mvk.Modbus.Tests.Pool"
dotnet test --filter "DisplayName~SaveConfiguration"
dotnet test tests/Mvk.MVK01.Tests --list-tests
```

`--filter "FullyQualifiedName~Retry"` — подстрока в полном имени теста
(`Mvk.Modbus.Tests.Pipeline.MiddlewareTests.Retry_TransientFailure_RetriesUntilSuccess`
и т.д.); в примере выше выполняются 3 теста. `--list-tests` печатает полные
имена всех обнаруженных тестов — удобно, чтобы составить фильтр.

### 6.3. Сколько идут и как читать вывод

На машине разработки (после сборки) прогон занимает:

| Проект | Число тестов | Длительность |
|---|---|---|
| `Mvk.Modbus.Tests` | 123 | ~2 с |
| `Mvk.MVK01.Tests` | 55 | ~0,7 с |

Полный `dotnet test` из чистого состояния включает сборку решения — это ещё
десятки секунд (восстановление пакетов и компиляция всех проектов). Итоговая
строка выглядит так:

```
Пройден!   : не пройдено     0, пройдено   123, пропущено     0, всего   123, длительность 2 s. - Mvk.Modbus.Tests.dll (net8.0)
```

Локализованный вывод зависит от языка SDK; в английской версии будет
`Passed!`. При падении теста выводится его полное имя, сообщение `Assert`
и стек; при необходимости повышайте подробность через
`dotnet test -v normal` или `-l "console;verbosity=detailed"`.

Особенность xunit: тестовые классы (коллекции) по умолчанию выполняются
параллельно. Поэтому в тестах нельзя полагаться на общий порт 502 —
каждый тест поднимает свой `MvkTestServer` на свободном порту.

---

## 7. Приёмы и советы

### 7.1. Свободный порт вместо 502

Всегда `MvkTestServer.Start()` без аргумента порта и `server.Port` — так тесты
не конфликтуют друг с другом и с реальными процессами. `GetFreePort()`
используйте только там, где порт должен быть известен заранее (негативный
сценарий или перезапуск сервера на том же порту).

### 7.2. Изоляция тестов

- один тест — один сервер: `await using MvkTestServer server = MvkTestServer.Start();`
- свой пул и своё устройство на каждый тест;
- не используйте статические поля с общим состоянием;
- файлы конфигурации пишите в уникальный временный каталог
  (`Path.Combine(Path.GetTempPath(), "my-tests-" + Guid.NewGuid().ToString("N"))`)
  и удаляйте в `Dispose`.

### 7.3. Короткие таймауты и отключение фона

Тест не должен ждать настройки по умолчанию. Рабочий набор для тестов:

| Настройка | Значение в тесте | Зачем |
|---|---|---|
| `ConnectTimeoutMs` | 300–1000 | Быстрый отказ недоступного адреса |
| `ReadTimeoutMs` | 300–500 | Быстрая проверка молчащего сервера |
| `MaxRetries` | 0 или 1 | Максимум попыток диктуется сценарием |
| `RetryDelayMs` | 1–10 | Не ждать backoff |
| `ReconnectBaseDelayMs` / `ReconnectMaxDelayMs` | 50 / 100–200 | Быстрый реконнект при проверке состояний |
| `HealthCheckIntervalSeconds` | 1 | Быстрый фоновый цикл |
| `ReconnectEnabled` | `false` по умолчанию | Детерминизм; включать только для тестов реконнекта |
| `IdleDisconnectSeconds` | 1 (если тестируется простой) | Быстрая проверка idle-отключения |

### 7.4. Реальное устройство — только вручную

Автотесты не должны обращаться к реальному МВК01: тест станет медленным,
нестабильным и потенциально опасным для стенда. По правилам проекта:

- автотесты — против `Mvk.Testing`;
- на железе — ручной smoke-тест (раздел 8), по умолчанию **только чтение**;
- `Save`/`Restore`/`Reboot` и запись регистров на реальном устройстве —
  только с явного разрешения.

### 7.5. Стабильность: ожидание вместо сна

Фоновые процессы (реконнект, обслуживание, статус команды) завершаются
не мгновенно. Не подбирайте `Task.Delay` — на медленной машине он станет
причиной flaky-тестов. Используйте опрос с дедлайном:

```csharp
private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
{
    DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;

    while (DateTimeOffset.UtcNow < deadline)
    {
        if (condition())
        {
            return true;
        }

        await Task.Delay(50);
    }

    return condition();
}
```

Пример использования (из `MvkDeviceTests`) — ожидание восстановления связи:

```csharp
transportAvailable = true;

Assert.True(await WaitUntilAsync(
    () => device.State == MvkDeviceState.Connected,
    TimeSpan.FromSeconds(5)));
```

Вместо безусловного `await Task.Delay(200)` лучше проверять условие с запасом
по времени: тест либо завершится сразу после наступления события, либо
осмысленно упадёт по таймауту.

### 7.6. Сравнение чисел с плавающей точкой

xunit умеет сравнивать с точностью:

```csharp
Assert.Equal(12.25f, channel.RmsX25kHzAcceleration, 6); // 6 знаков после запятой
```

Для `float`-значений из регистров это обязательный приём: даже корректно
декодированное значение редко совпадает «до последнего бита».

### 7.7. Что проверять в первую очередь

- Прикладной код: правильные адреса/количество регистров в `Requests`,
  обработка `MvkModbusException` по `ErrorKind`, реакция UI/MVVM на ошибки.
- Пайплайн: порядок, короткое замыкание, свои middleware.
- Состояние пула: `State`, `IsConnected`, события `StateChanged`.
- Команды МВК01: кодирование команды, распознавание статусов, таймаут.

---

## 8. Ограничения эмулятора и что проверять на реальном устройстве

| Поведение | Эмулятор | Где проверять |
|---|---|---|
| Чётность адреса и количества (ошибка Modbus 0x01 на нечётный запрос) | не проверяет | реальное устройство; на клиенте — `ValidationMiddleware` с `RequireEvenAddressAndQuantity` |
| Реальные значения параметров и точность кодирования | подставляет то, что записали | реальное устройство (сверка с прямым чтением) |
| Порядок байт (регистр `0x000C`) и swap-строки cp1251 | не воспроизводит | реальное устройство |
| Длительность `SaveConfiguration` до 3 с | отвечает мгновенно | реальное устройство; таймаут команды — `FakeModbusTransport` и короткий таймаут операции |
| Ошибки Flash, смена статуса команды, сбойные состояния | не воспроизводит | `FakeModbusTransport` (программируемый статус) |
| Обрыв связи и переподключение | только остановкой/запуском сервера | реальное устройство или `SilentTcpServer` |

### Ручной smoke-тест на реальном устройстве

Адрес стенда — `10.20.0.211:502` (SlaveId 1, BigEndian). Сначала проверьте
доступность:

```powershell
Test-NetConnection 10.20.0.211 -Port 502
```

Минимальный smoke-тест — чтение (без записи):

```csharp
using Mvk.MVK01;

await using Mvk01Connection mvk = await Mvk01Connection.ConnectAsync("10.20.0.211");
// порт 502, SlaveId 1, BigEndian, таймауты и реконнект — по умолчанию

Mvk01DeviceInfo info = await mvk.ReadDeviceInfoAsync();
Console.WriteLine($"ПО: {info.FirmwareVersion}, серийный: {info.SerialNumber}");

Mvk01Channel channel = await mvk.ReadChannelAsync(1);
Console.WriteLine($"Напряжение: {channel.SensorVoltage:F4} В, СКЗ X…25кГц: {channel.RmsX25kHzAcceleration:F3} м/с2");
```

На железе имеет смысл дополнительно убедиться, что:

- нечётный адрес/количество действительно отклоняются (ответ-исключение);
- `ReadChannelAsync(1)` на реальном устройстве возвращает ненулевые значения,
  совпадающие с чтением по номерам параметров;
- строки (единицы измерения, серийные номера датчиков) читаются корректно
  после swap-декодирования (cp1251);
- команды выполняются с реальными таймингами (не запускайте `Save`/`Reboot`
  без явного разрешения).

---

## Подводные камни

- **Порт `502` по умолчанию нигде не гарантирован.** `MvkTestServer.Start()`
  использует `port = 0`, фактический порт — в `server.Port`. Забыть передать
  его в `Defaults.Port` — самая частая ошибка первого теста.
- **`GetFreePort` — не бронь.** Между получением свободного порта и `Start(port)`
  порт может занять другой процесс; для изоляции используйте `port = 0`.
- **Эмулятор не отвергает нечётные адреса.** Тест «нечётный адрес» против
  `MvkTestServer` пройдёт успешно, хотя реальное МВК01 вернёт ошибку Modbus
  `0x01`. Проверку чётности делайте через `ValidationMiddleware` на клиенте,
  а поведение устройства — на железе.
- **`FakeModbusTransport.IsConnected` не сбрасывается при ошибке ответа.**
  Он становится `false` только после `DisconnectAsync`/`Dispose` (и `true` —
  после успешного ответа). Не используйте его как индикатор «последний запрос
  был успешен».
- **Обработчик `FakeModbusTransport` синхронный.** Им нельзя проверить
  таймауты, отмену и конкурентность. Для задержки нужен собственный
  `IMvkTransport` (в тестах библиотеки — `FakeTransport` с `delay`), для
  таймаута — `SilentTcpServer`.
- **`SilentTcpServer` принимает соединение.** Поэтому ошибка именно
  `MvkErrorKind.Timeout`, а не `Transport`. Если нужна ошибка подключения,
  направьте устройство на свободный порт от `GetFreePort()`.
- **Настройки по умолчанию делают тесты медленнее.** `MaxRetries = 2` означает
  три попытки, `RetryDelayMs = 150` — паузы между ними. В негативных тестах
  ставьте `MaxRetries = 0` там, где повтор не проверяется.
- **`ReconnectEnabled = true` по умолчанию.** В тестах это лишний фон:
  отключайте (`Pool = new MvkPoolOptions { ReconnectEnabled = false }`) или
  проверяйте состояние через `WaitUntilAsync`, а не через `Task.Delay`.
- **`middlewares: []` в конструкторе пула отключает проверки.** Удобно для
  изоляции логики, но тест перестаёт проверять `validation` и
  `writeProtection`. Сравнивайте с DI-сценарием, если поведение пайплайна
  важно.
- **`Assert.Equal` без точности для `float` — источник ложных падений.**
  Для значений из регистров указывайте precision.
- **`MvkTestServer.DisposeAsync` подавляет исключения.** Если тест «завис»,
  причина не в остановке сервера: ищите незавершённый запрос или ожидание.
- **Параллельный запуск xunit.** Тестовые классы идут параллельно, поэтому
  общие порты, файлы и статические объекты недопустимы.

## См. также

- [Конфигурация](03-configuration.md) — `Defaults`, `Devices`, валидация настроек
- [Пул устройств](05-device-pool.md) — состояния, реконнект, сериализация запросов
- [Ошибки](08-errors-logging-metrics.md) — `MvkErrorKind`, `IsTransient`, логирование и метрики
- [README](../README.md) — краткий справочник и быстрый старт
