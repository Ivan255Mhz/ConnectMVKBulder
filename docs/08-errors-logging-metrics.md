# Глава 08. Ошибки, логирование и метрики

Надёжность работы с МВК01 держится на трёх вещах: понятной модели ошибок, наблюдаемости
(логи и метрики) и предсказуемом восстановлении связи. Библиотека не «прячет» сбои и не
возвращает пустые массивы вместо данных:

- каждая неудача обращения к устройству приходит либо как `ModbusResponse` с ошибкой,
  либо как `MvkModbusException` с категорией `MvkErrorKind`;
- отмена вызывающим кодом — это отдельное исключение `OperationCanceledException`,
  а не «ошибка устройства»;
- связь восстанавливается автоматически: транспорт закрывает сокет при любой ошибке,
  а пул планирует реконнект с экспоненциальной задержкой.

Эта глава — справочник по диагностике: полная карта `MvkErrorKind`, преобразование
исключений NModbus/.NET в `MvkModbusException`, контракты обработки, категории и тексты
логов, метрики `System.Diagnostics.Metrics` и готовые рецепты разбора типовых сбоев.

---

## 1. Модель ошибок: `MvkModbusException` и `MvkErrorKind`

### 1.1. Конструкторы и свойства

`MvkModbusException` — единственный тип исключения, который библиотека создаёт для ошибок
работы с устройством. Полный набор конструкторов:

```csharp
public MvkModbusException(string message);

public MvkModbusException(string message, MvkErrorKind errorKind);

public MvkModbusException(string message, MvkErrorKind errorKind, Exception? innerException);

public MvkModbusException(
    string message,
    MvkErrorKind errorKind,
    string? deviceId,
    byte? modbusExceptionCode = null,
    Exception? innerException = null);
```

Конструктор без `errorKind` даёт `MvkErrorKind.Unknown`, конструктор без `deviceId` —
`DeviceId == null`.

| Свойство | Тип | Описание |
|---|---|---|
| `ErrorKind` | `MvkErrorKind` | Категория ошибки; главный признак для обработки |
| `DeviceId` | `string?` | Идентификатор устройства из конфигурации; может быть `null` (например, ошибки загрузчика конфигурации) |
| `ModbusExceptionCode` | `byte?` | Код ошибки Modbus из ответа-исключения; заполняется **только** при `MvkErrorKind.DeviceError` |
| `IsTransient` | `bool` | Вычисляемое: `ErrorKind is Transport or Timeout or Busy` |
| `Message` | `string` | Текст ошибки (унаследовано от `Exception`) |
| `InnerException` | `Exception?` | Исходное исключение NModbus/.NET, если ошибка получена преобразованием |

Правило: **для решения «повторять или нет» используйте `IsTransient`, а не сравнение
`ErrorKind` вручную.** Это тот же признак, по которому работает `RetryMiddleware`.

### 1.2. Таблица всех значений `MvkErrorKind`

| Значение (номер) | Когда возникает | `IsTransient` | Что делать |
|---|---|---|---|
| `Unknown` (0) | `MvkPipeline.ExecuteAsync`: цепочка завершилась, а `context.Response` не заполнен («Пайплайн завершился без ответа…»); конструктор исключения без категории | нет | Ошибка в собственном middleware: он не вызвал `next` и не сформировал ответ. Исправьте middleware |
| `Transport` (1) | Сбой сокета/сети: `SocketException` при подключении, `ConnectionReset` и т.п.; фолбэк `MapException` для нераспознанных исключений; `ModbusResponse.Failure(Exception)` | да | Связь. Ждите автоматический реконнект, повторяйте запрос позже; проверьте кабель, сеть, питание |
| `Timeout` (2) | Истёк `ConnectTimeoutMs`; истёк `ReadTimeoutMs`/`WriteTimeoutMs` (`TimeoutException`, `IOException` с «timeout», `SocketException` с `TimedOut`); превышен бюджет операции `Items[MvkContextItems.TimeoutMs]` (`TimeoutMiddleware`) | да | Связь или медленная операция. Увеличьте таймауты; при повторах ждите восстановления |
| `DeviceError` (3) | Устройство вернуло ответ-исключение Modbus (`NModbus.SlaveException`); код доступен в `ModbusExceptionCode` | нет | Ошибка данных/запроса. Повторять бессмысленно: исправьте адрес, количество регистров, функцию или состояние устройства |
| `DeviceNotFound` (4) | `MvkDevicePool.GetDevice(id)` / `IMvk01DeviceFactory.GetDevice(id)` — идентификатора нет в пуле | нет | Ошибка конфигурации/кода. Используйте `TryGetDevice`, `AddDevice` или правильный `Id` |
| `DeviceDisabled` (5) | Устройство выключено в конфигурации (`Enabled = false`): `ExecuteAsync` возвращает `Failure`, `EnsureConnectedAsync` бросает исключение | нет | Включите устройство в конфигурации либо не опрашивайте его |
| `Validation` (6) | `ValidationMiddleware`: нет данных записи, нечётные адрес/количество при `RequireEvenAddressAndQuantity`; `WriteProtectionMiddleware`: `ReadOnly`, защищённая область конфигурации; транспорт: операция не поддерживается | нет | Ошибка запроса или политики. Исправьте запрос/настройки; повтор не поможет |
| `Canceled` (7) | В текущей версии библиотека это значение **не создаёт**: отмена всегда приходит как `OperationCanceledException` (см. 1.4). Значение зарезервировано — им может пометить ошибку свой middleware/транспорт | нет | Штатное завершение операции, а не сбой |
| `Busy` (8) | Зарезервировано под «устройство занято» (очередь/переполнение). Встроенные компоненты его не создают: `MvkDevice` сериализует запросы через `SemaphoreSlim` без таймаута ожидания | да | Если такой код возвращает ваш middleware/транспорт — `RetryMiddleware` повторит запрос |
| `Configuration` (9) | `MvkModbusOptionsLoader` (файл/секция/валидация), `MvkMiddlewareResolver` (неизвестное имя в `Pipeline`), `MvkMiddlewareRegistry.Add` (тип не `IModbusMiddleware`), `MvkDevicePool.AddDevice` (пустой `Id`, дубликат `Id`) | нет | Ошибка конфигурации. Исправляется до запуска; в продакшене — fail fast |

Полное перечисление (совпадает с исходником `src/Mvk.Modbus/MvkErrorKind.cs`):

```csharp
public enum MvkErrorKind
{
    Unknown = 0,
    Transport = 1,
    Timeout = 2,
    DeviceError = 3,
    DeviceNotFound = 4,
    DeviceDisabled = 5,
    Validation = 6,
    Canceled = 7,
    Busy = 8,
    Configuration = 9
}
```

### 1.3. Перехват по видам

Типовой шаблон прикладного кода: отмена, временная ошибка, ошибка устройства, всё
остальное — в этом порядке.

```csharp
try
{
    ushort[] data = await device.ReadHoldingRegistersAsync(0x1004, 2, ct);
}
catch (OperationCanceledException) when (ct.IsCancellationRequested)
{
    // Штатная остановка (Ctrl+C, закрытие приложения) — не ошибка связи.
    throw;
}
catch (MvkModbusException ex) when (ex.IsTransient)
{
    // Transport / Timeout / Busy: связь. Реконнект уже идёт в фоне (Pool.ReconnectEnabled = true).
    logger.LogWarning("МВК '{DeviceId}': временная ошибка {ErrorKind} — {Message}",
        ex.DeviceId, ex.ErrorKind, ex.Message);
}
catch (MvkModbusException ex) when (ex.ErrorKind == MvkErrorKind.DeviceError)
{
    // Устройство ответило кодом Modbus: ошибка данных, а не связи.
    logger.LogError("МВК '{DeviceId}': Modbus 0x{Code:X2} — {Message}",
        ex.DeviceId, ex.ModbusExceptionCode, ex.Message);
}
catch (MvkModbusException ex)
{
    // Validation, DeviceDisabled, Configuration, DeviceNotFound, Unknown:
    // требуют изменения запроса, настроек или кода.
    logger.LogError("МВК '{DeviceId}': {ErrorKind} — {Message}", ex.DeviceId, ex.ErrorKind, ex.Message);
}
```

Вариант «без исключений» — через `ExecuteAsync` и проверку `response.Error`:

```csharp
ModbusResponse response = await device.ExecuteAsync(
    ModbusRequest.ReadHoldingRegisters("mvk-1", 0x1004, 2), ct);

if (!response.IsSuccess)
{
    MvkModbusException error = response.Error!;

    switch (error.ErrorKind)
    {
        case MvkErrorKind.Transport:
        case MvkErrorKind.Timeout:
        case MvkErrorKind.Busy:
            // временная — ждём реконнект
            break;
        case MvkErrorKind.DeviceError:
            // код Modbus в error.ModbusExceptionCode
            break;
        default:
            // ошибка данных/конфигурации — эскалируем
            break;
    }
}
```

### 1.4. Отмена — не ошибка

Отмена (`CancellationToken`) — это отдельный механизм со своим исключением:

| Ситуация | Что видит вызывающий код |
|---|---|
| `ct` отменён до/во время запроса | `OperationCanceledException`; ответ не формируется |
| `ct` отменён во время ожидания между повторами `RetryMiddleware` | `OperationCanceledException` из `Task.Delay` |
| Истёк `Items[MvkContextItems.TimeoutMs]` (бюджет операции) | `ModbusResponse` с `MvkErrorKind.Timeout` — это ошибка, а не отмена |
| Истёк сокетный таймаут (`ReadTimeoutMs` и т.п.) | `ModbusResponse` с `MvkErrorKind.Timeout` |

`TimeoutMiddleware` перехватывает `OperationCanceledException` и превращает его в
`Timeout`-**ответ** только тогда, когда внешний токен не отменён. Если отменён внешний
токен — исключение уходит наверх без изменений.

Практические правила:

1. В `catch (OperationCanceledException)` проверяйте `ct.IsCancellationRequested`:
   если токен отменён — это плановое завершение, не логируйте его как `Error`.
2. Не «проглатывайте» отмену и не возвращайте `null`/пустой массив — фоновые циклы
   не завершатся.
3. Помните о неопределённости для записи: если отмена произошла после отправки команды,
   но до ответа, состояние устройства неизвестно — команда могла выполниться.
4. `MvkErrorKind.Canceled` в текущей версии библиотека не генерирует; ориентируйтесь на
   `OperationCanceledException`.

### 1.5. Вложенные исключения

`InnerException` хранит исходное исключение, из которого получена ошибка. Это важно для
диагностики: текст `MvkModbusException` — краткая сводка, а первопричина лежит внутри.

| Источник `MvkModbusException` | Что в `InnerException` |
|---|---|
| `MapException`, таймаут | `TimeoutException`, `IOException`, `SocketException` |
| `MapException`, ответ-исключение Modbus | `NModbus.SlaveException` (в нём `FunctionCode`, `SlaveExceptionCode`, `SlaveAddress`) |
| `MapException`, фолбэк | Любое исходное исключение (например, `InvalidOperationException`) |
| Ошибка подключения `ConnectAsync` | `SocketException` (или другое исключение подключения) |

Цепочку можно пройти самому, если нужно достать, например, `SocketError`:

```csharp
catch (MvkModbusException ex)
{
    Exception? root = ex.InnerException;
    while (root?.InnerException is not null)
    {
        root = root.InnerException;
    }

    logger.LogError(ex, "МВК '{DeviceId}': {ErrorKind}, первопричина: {Root}",
        ex.DeviceId, ex.ErrorKind, root?.GetType().Name ?? "нет");
}
```

Не анализируйте ошибки по тексту `Message`: тексты предназначены для человека, а код
должен опираться на `ErrorKind`, `ModbusExceptionCode` и типы вложенных исключений.

---

## 2. Как исключения NModbus/.NET превращаются в `MvkModbusException`

### 2.1. Точка преобразования

Преобразование выполняет `NModbusTransport.MapException` (`src/Mvk.Modbus/Transport/NModbusTransport.cs`).
Алгоритм:

1. обход всей цепочки `InnerException` (от внешнего к вложенным) с проверкой типов;
2. первый подходящий случай задаёт категорию;
3. если совпадений нет — проверяется `IOException` с подстрокой «timeout»/«timed out»;
4. иначе — категория `Transport` с текстом общего вида и исходным исключением внутри.

Ошибки подключения (в `ConnectAsync`) преобразуются отдельно:

| Ситуация | Категория | Текст |
|---|---|---|
| Истёк `ConnectTimeoutMs` | `Timeout` | `Таймаут подключения к {Endpoint} ({ConnectTimeoutMs} мс).` |
| `SocketException` и прочие исключения при подключении | `Transport` | `Не удалось подключиться к {Endpoint}: {сообщение исключения}` |
| Отмена внешним токеном | — | `OperationCanceledException` пробрасывается как есть |

### 2.2. Карта соответствий

| Исключение NModbus/.NET | `MvkErrorKind` | `ModbusExceptionCode` | Текст (шаблон) |
|---|---|---|---|
| `TimeoutException` (в том числе вложенное) | `Timeout` | — | `Таймаут при обращении к {Endpoint}.` |
| `IOException` с «timeout»/«timed out» в `Message` | `Timeout` | — | `Таймаут при обращении к {Endpoint}.` |
| `SocketException` с `SocketErrorCode == TimedOut` | `Timeout` | — | `Таймаут при обращении к {Endpoint}.` |
| `NModbus.SlaveException` (в том числе вложенное) | `DeviceError` | `SlaveExceptionCode` | `Устройство '{Id}' вернуло ошибку Modbus 0x{Код:X2} (функция 0x{FunctionCode:X2}).` |
| `SocketException` прочие (`ConnectionReset`, `ConnectionRefused`, …) | `Transport` | — | `Ошибка при обращении к {Endpoint}: {сообщение}` |
| Любое другое исключение | `Transport` | — | `Ошибка при обращении к {Endpoint}: {сообщение}` |

Соответствия закреплены тестами `tests/Mvk.Modbus.Tests/Transport/NModbusTransportTests.cs`:
`TimeoutException`, `IOException("Read timed out")`, `IOException`, оборачивающий
`TimeoutException`, и `SocketException(SocketError.TimedOut)` дают `Timeout`;
`SocketException(ConnectionReset)` и произвольный `InvalidOperationException` — `Transport`.

Обратите внимание: `IOException` без признака таймаута отдельной ветки не имеет и попадает
в фолбэк `Transport` (в `InnerException` остаётся исходное исключение) — это ожидаемое
поведение, а не потеря информации.

### 2.3. Пример: ошибка Modbus 0x01 при нечётном запросе

МВК01 принимает только запросы с чётным адресом и чётным числом регистров. Если локальная
проверка выключена (`RequireEvenAddressAndQuantity = false`), запрос уходит на устройство:

```csharp
// Адрес 0x2001 и количество 1 — оба нечётные; локальная проверка выключена.
ModbusResponse response = await device.ExecuteAsync(
    ModbusRequest.ReadHoldingRegisters("mvk-1", 0x2001, 1));

// response.IsSuccess                  == false
// response.Error.ErrorKind            == MvkErrorKind.DeviceError
// response.Error.IsTransient          == false
// response.Error.ModbusExceptionCode  == 0x01
// response.Error.Message             == "Устройство 'mvk-1' вернуло ошибку Modbus 0x01 (функция 0x83)."
```

Про «функцию 0x83»: `NModbusTransport` подставляет в текст `SlaveException.FunctionCode` —
код функции из ответа-исключения, где выставлен старший бит `0x80`. Для чтения регистров
хранения (0x03) это `0x83`, для записи нескольких регистров (0x10) — `0x90`. Код самой
ошибки (0x01) при этом лежит в `ModbusExceptionCode`.

Если проверка включена (так делает `AddMvk01Devices()` и `Mvk01Connection`), запрос до
устройства не доходит:

```csharp
// response.Error.ErrorKind == MvkErrorKind.Validation
// response.Error.Message   == "Адрес 0x2001 и число регистров 1 должны быть чётными
//                             (особенность устройства 'mvk-1')."
```

Первый вариант — ошибка данных от устройства («так нельзя»), второй — ошибка валидации
до сети. Диагностически они разные: `DeviceError` означает, что запрос ушёл и был
отклонён железом; `Validation` — что библиотека отклонила его сама.

### 2.4. Коды исключений Modbus и `ModbusExceptionCode`

`ModbusExceptionCode` заполняется только для `MvkErrorKind.DeviceError`. Типовые коды:

| Код | Название | Когда бывает | Что делать |
|---|---|---|---|
| `0x01` | `IllegalFunction` | МВК01: нечётные адрес/количество регистров (см. 2.3); неподдерживаемая функция | Включить `RequireEvenAddressAndQuantity`, читать парами регистров |
| `0x02` | `IllegalDataAddress` | Адрес вне карты регистров, чтение недоступной области | Сверить адрес с `Mvk01RegisterMap` / `MVK01_registers.txt` |
| `0x03` | `IllegalDataValue` | Неверное количество регистров, некорректная структура запроса | Проверить `Quantity` (для чтения 1–125, для записи 1–123) |
| `0x04` | `SlaveDeviceFailure` | Внутренняя ошибка устройства | Повторить позже; при постоянстве — диагностика железа |
| `0x05` | `Acknowledge` | Команда принята, обрабатывается | NModbus сам ожидает и перечитывает ответ |
| `0x06` | `SlaveDeviceBusy` | Устройство занято | NModbus повторяет; при `SlaveBusyUsesRetryCount` ограниченно |

Пример обработки конкретного кода:

```csharp
ModbusResponse response = await device.ExecuteAsync(
    ModbusRequest.ReadHoldingRegisters("mvk-1", 0x2001, 1));

if (!response.IsSuccess && response.Error!.ErrorKind == MvkErrorKind.DeviceError)
{
    Console.WriteLine($"Устройство ответило Modbus 0x{response.Error.ModbusExceptionCode:X2}: "
        + response.Error.Message);
}
```

---

## 3. Контракты обработки ошибок

### 3.1. Что возвращает, а что бросает

| Метод/член | Ошибка связи и устройства | Ошибка конфигурации/кода |
|---|---|---|
| `IMvkDevice.ExecuteAsync(request[, timeout], ct)` | `ModbusResponse.Failure(error)` — проверяйте `IsSuccess` | `DeviceDisabled` — тоже `Failure`; `ObjectDisposedException` — вызов у освобождённого устройства |
| Обёртки `ReadHoldingRegistersAsync`, `WriteSingleRegisterAsync`, … | `throw MvkModbusException` (возвращается `response.Data` только при успехе) | То же |
| `IMvkDevice.EnsureConnectedAsync(ct)` | `throw MvkModbusException` (`Transport`/`Timeout`) | `throw MvkModbusException` (`DeviceDisabled`) |
| `IMvkDevicePool.GetDevice(id)` / `IMvk01DeviceFactory.GetDevice(id)` | — | `throw MvkModbusException` (`DeviceNotFound`) |
| `MvkDevicePool.AddDevice(options)` | — | `throw MvkModbusException` (`Configuration`: пустой `Id`, дубликат) |
| `Mvk01Device` команды (`SaveConfigurationAsync`, `RebootAsync`, …) | `Mvk01CommandResult` со статусом `TransportError`/`Timeout` | `Mvk01CommandResult` со статусом `BadCommand`/`FlashError` |
| `Mvk01Device` чтения | `throw MvkModbusException` (через обёртки базового устройства) | `ArgumentOutOfRangeException` — канал вне 1–8, тахометр вне 1–2, номер параметра вне 1–184 |

Исключения вне модели ошибок (не `MvkModbusException`) — это ошибки программирования,
и обрабатывать их как связь нельзя:

- `ArgumentException` при создании `ModbusRequest`: «Идентификатор устройства обязателен.»,
  «Число записываемых регистров должно быть от 1 до 123, получено …»;
- `ArgumentOutOfRangeException`: «Число регистров должно быть от 1 до 125.», «Диапазон
  адресов выходит за границы адресного пространства Modbus.», «Номер канала должен быть
  в диапазоне 1..8.» и т.п.;
- `ObjectDisposedException` — устройство/пул освобождены;
- `OptionsValidationException` (DI) — конфигурация не прошла `ValidateOnStart`.

### 3.2. Отключённое устройство и отсутствующий идентификатор

```csharp
// Enabled = false: ExecuteAsync не открывает сокет и возвращает ошибку.
ModbusResponse response = await device.ExecuteAsync(
    ModbusRequest.ReadHoldingRegisters("mvk-1", 0x1004, 2));
// response.Error.ErrorKind == MvkErrorKind.DeviceDisabled
// response.Error.Message    == "Устройство 'mvk-1' отключено в конфигурации (Enabled = false)."

// EnsureConnectedAsync в этой ситуации бросает то же исключение.
try
{
    await device.EnsureConnectedAsync();
}
catch (MvkModbusException ex)
{
    // ex.ErrorKind == MvkErrorKind.DeviceDisabled — сокет не открывался.
}
```

```csharp
// Идентификатора нет в пуле.
try
{
    _ = pool.GetDevice("unknown");
}
catch (MvkModbusException ex)
{
    // ex.ErrorKind == MvkErrorKind.DeviceNotFound
    // ex.DeviceId  == "unknown"
    // ex.Message   == "Устройство 'unknown' не найдено в конфигурации пула."
}
```

### 3.3. Системные команды МВК01: результат вместо исключения

`Mvk01Device.ExecuteSystemCommandAsync` (и обёртки `SaveConfigurationAsync`,
`RestoreConfigurationAsync`, `RebootAsync`) не бросают исключение при неуспехе команды —
они возвращают `Mvk01CommandResult`:

| `Mvk01CommandStatus` | Значение | Смысл | `IsSuccess` |
|---|---|---|---|
| `Success` | `0x00000` | Команда выполнена | да |
| `BadCommand` | `0x00BAD` | Клиент записал неверную команду | нет |
| `FlashError` | `0xF0BAD` | Ошибка флэш-памяти, конфигурация не сохранена | нет |
| `Pending` | `-1` | Статус пока не определён (продолжается ожидание) | нет |
| `Timeout` | `-2` | Истёк таймаут ожидания статуса | нет |
| `TransportError` | `-3` | Ошибка транспорта при записи или чтении статуса | нет |

```csharp
Mvk01CommandResult result = await device.SaveConfigurationAsync();

if (!result.IsSuccess)
{
    // result.Error != null для TransportError; result.DeviceStatus — сырой статус, если прочитан.
    logger.LogError("МВК '{Id}': команда {Command} не выполнена: {Status} ({Error})",
        device.Id, result.Command, result.Status, result.Error?.Message ?? "без ошибки транспорта");
}
```

Текст `result.ToString()`: `"Команда {Command}: {Status}"`, а при наличии ошибки —
`"Команда {Command}: {Status} ({Error.Message})"`.

### 3.4. Стратегии прикладного кода

| Категория | Стратегия | Чего не делать |
|---|---|---|
| `Transport`, `Timeout`, `Busy` (`IsTransient`) | Не эскалировать; подождать реконнект и повторить по расписанию; на UI показывать «нет связи» | Не считать это ошибкой данных; не менять адреса/команды |
| `DeviceError` | Зафиксировать `ModbusExceptionCode` и исправить запрос | Не повторять: повтор вернёт тот же код |
| `Validation` | Исправить запрос/политику записи (`WritePolicy`, `AllowConfigurationWrite`, чётность) | Не «обходить» middleware отключением пайплайна |
| `Configuration`, `DeviceNotFound`, `DeviceDisabled` | Ошибка настройки: исправить конфигурацию/код; при старте — fail fast | Не пытаться лечить реконнектом |
| `OperationCanceledException` | Штатное завершение: пробросить, не логировать как сбой | Не подменять на пустой результат |
| Записи и команды Save/Reboot | Повторять только осознанно: операция может выполниться до таймаута | Не запускать самоповтор в цикле |

Механику кратковременных сбоев (до `MaxRetries + 1` попыток с линейной задержкой
`RetryDelayMs`) уже берёт на себя `RetryMiddleware`. Прикладному коду остаётся стратегия
на длительные перерывы связи: дождаться восстановления и выполнить запрос заново
(готовый метод — в разделе 7.1).

---

## 4. Логирование

### 4.1. Категории

Библиотека пишет только через `ILogger<T>`; категория — полное имя типа. Если
`ILoggerFactory` не передан (автономная сборка `MvkDevicePool`/`NModbusTransport` без
DI), используется `NullLogger`, и логи молчат — это не ошибка.

| Категория | Где | Что пишет |
|---|---|---|
| `Mvk.Modbus.Transport.NModbusTransport` | Транспорт | `Debug` — установка соединения; `Warning` — каждая ошибка обращения (с исключением) |
| `Mvk.Modbus.Pipeline.Middlewares.LoggingMiddleware` | Middleware `logging` | `Debug` — успешный запрос; `Warning` — неуспешный |
| `Mvk.Modbus.Pool.MvkDevice` | Устройство пула | `Information` — восстановление соединения; `Debug` — неудачный реконнект, закрытие по простою |
| `Mvk.Modbus.Pool.MvkDevicePool` | Пул | `Debug` — добавление устройства; `Warning` — ошибка обслуживания |
| `Mvk.Modbus.DependencyInjection.MvkModbusWarmupHostedService` | `AddMvkModbusHostedService()` | `Information` — состав пула при старте |

Типизированный слой `Mvk.MVK01` собственного логирования не ведёт: его ошибки видны
через транспорт (`NModbusTransport`) и пул (`MvkDevice`), потому что все запросы идут
через те же компоненты. Категория `Mvk.MVK01.*` появится только в вашем коде поверх
фасада, если вы сами создадите `ILogger<T>` в слое приложения.

### 4.2. Кто что пишет: точные тексты

| Компонент | Уровень | Текст (шаблон) |
|---|---|---|
| `NModbusTransport` | `Debug` | `МВК '{DeviceId}': соединение установлено [{Endpoint}], SlaveId {SlaveId}.` |
| `NModbusTransport` | `Warning` | `МВК '{DeviceId}': ошибка обращения [{Endpoint}].` (вместе с исключением) |
| `LoggingMiddleware` | `Debug` | `МВК '{DeviceId}': {Operation} 0x{Address:X4} x{Quantity} — успех за {ElapsedMs:F1} мс (попытка {Attempt}).` |
| `LoggingMiddleware` | `Warning` | `МВК '{DeviceId}': {Operation} 0x{Address:X4} x{Quantity} — ошибка {ErrorKind} за {ElapsedMs:F1} мс (попытка {Attempt}): {Message}` |
| `MvkDevice` | `Information` | `МВК '{DeviceId}': соединение восстановлено [{Endpoint}].` |
| `MvkDevice` | `Debug` | `МВК '{DeviceId}': реконнект не удался — {ErrorKind}: {Message}` |
| `MvkDevice` | `Debug` | `МВК '{DeviceId}': соединение закрыто после простоя ({IdleSeconds} с).` |
| `MvkDevicePool` | `Debug` | `МВК '{DeviceId}': устройство добавлено в пул [{Endpoint}], Enabled={Enabled}.` |
| `MvkDevicePool` | `Warning` | `Ошибка обслуживания пула устройств МВК.` (вместе с исключением) |
| `MvkModbusWarmupHostedService` | `Information` | `Пул МВК инициализирован: устройств {DeviceCount} ({Devices}).` |

Пример строки `LoggingMiddleware` после подстановки значений:

```text
МВК 'mvk-1': ReadHoldingRegisters 0x1004 x2 — успех за 12,4 мс (попытка 1).
МВК 'mvk-1': ReadHoldingRegisters 0x2001 x1 — ошибка Validation за 0,3 мс (попытка 1): Адрес 0x2001 и число регистров 1 должны быть чётными (особенность устройства 'mvk-1').
```

Одна и та же неудача даёт две записи: `Warning` транспорта («ошибка обращения») и
`Warning` `LoggingMiddleware` («ошибка {ErrorKind}…»). Это не дублирование по ошибке —
транспорт логирует факт сокетной ошибки, middleware — операцию целиком, с адресом,
количеством, длительностью и номером попытки.

### 4.3. Минимальная настройка (только консоль)

Хост `Host.CreateApplicationBuilder` уже добавляет консольный провайдер. Достаточно
настроить уровни:

```csharp
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// Ошибки видны всегда: транспорт и LoggingMiddleware пишут Warning.
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Успешные запросы видны на Debug — включаем точечно.
builder.Logging.AddFilter("Mvk.Modbus.Pipeline.Middlewares.LoggingMiddleware", LogLevel.Debug);

// Жизненный цикл устройств пула: реконнект, простой, ошибки обслуживания.
builder.Logging.AddFilter("Mvk.Modbus.Pool", LogLevel.Debug);
```

То же через `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Mvk.Modbus": "Warning",
      "Mvk.Modbus.Pipeline.Middlewares.LoggingMiddleware": "Debug",
      "Mvk.Modbus.Pool": "Debug"
    }
  }
}
```

Фильтр по префиксу категории действует на все вложенные категории: `"Mvk.Modbus"`
покрывает и транспорт, и пул, и middleware.

### 4.4. Логирование в файл через Serilog

Библиотека не зависит от Serilog — провайдер подключает приложение. Нужны пакеты
`Serilog.Extensions.Hosting`, `Serilog.Sinks.Console`, `Serilog.Sinks.File` (или их
аналоги). Провайдер можно оставить только Serilog, чтобы не было двойного вывода:

```csharp
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Mvk.Modbus.Pipeline.Middlewares.LoggingMiddleware", LogEventLevel.Debug)
    .MinimumLevel.Override("Mvk.Modbus.Pool", LogEventLevel.Debug)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        path: "logs/mvk-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

    builder.Logging.ClearProviders();                       // убираем стандартный консольный провайдер
    builder.Services.AddSerilog(Log.Logger, dispose: true);

    builder.Configuration.AddJsonFile(
        Path.Combine(AppContext.BaseDirectory, "mvkmodbus.json"), optional: false);

    builder.Services.AddMvk01(builder.Configuration);
    builder.Services.AddMvkModbusHostedService();

    using IHost host = builder.Build();
    await host.StartAsync();

    // ... работа с устройствами ...

    await host.StopAsync();
}
finally
{
    await Log.CloseAndFlushAsync();
}
```

`{SourceContext}` в шаблоне — это категория (`Mvk.Modbus.…`), поэтому фильтры и поиск по
логу работают так же, как с обычными провайдерами. Ошибки пишутся с исключением:
`{Exception}` выводит полную цепочку, включая `InnerException` (`SocketException`,
`SlaveException` и т.д.), — именно она нужна для первопричины.

### 4.5. Что включить в продакшене

- **Базовый уровень — `Information`.** Ошибки обращения (`Warning`) видны без
  дополнительных настроек: их пишут транспорт и `LoggingMiddleware`.
- **`LoggingMiddleware` — `Debug`** только если нужна построчная история запросов.
  Каждый успешный запрос — строка; на интенсивном опросе объём быстро растёт.
- **`Mvk.Modbus.Pool` — `Debug`** для разбора реконнектов и простоев. Важно: неудачные
  реконнекты пишутся на `Debug`, поэтому при уровне `Information` их в логе не будет —
  видно только успешное восстановление (`Information`).
- **`MvkDevicePool` и hosted-сервис** — оставьте `Information`/`Debug`: состав пула и
  ошибки обслуживания полезны при старте.
- **События `StateChanged`** логируйте в приложении: переходы `Connected → Faulted` и
  обратно — хороший журнал доступности, не зависящий от уровня `Debug`.
- **Ротация и срок хранения.** Файловый лог с суточной ротацией и лимитом файлов; при
  частых сбоях связи лог растёт быстро.
- **Не дублируйте ошибки.** Если `LoggingMiddleware` и транспорт уже пишут `Warning`,
  не добавляйте третий `Warning` в собственный `catch` на каждый запрос — иначе один
  сбой даст три строки. Свой `catch` уместен для смены уровня/категории или агрегации.

---

## 5. Метрики

### 5.1. Инструменты `MetricsMiddleware`

`MetricsMiddleware` публикует метрики через `System.Diagnostics.Metrics`, полностью
совместимые с OpenTelemetry, `dotnet-counters` и `MeterListener`. Инструменты статические:
создаются один раз на процесс, все пулы и устройства пишут в них.

| Инструмент | Тип | Единица | Теги |
|---|---|---|---|
| `mvk.modbus.requests` | `Counter<long>` | — | `device`, `operation`, `result` |
| `mvk.modbus.duration` | `Histogram<double>` | мс | `device`, `operation`, `result` |

Meter: `new Meter("Mvk.Modbus", "1.0.0")`.

| Тег | Значение |
|---|---|
| `device` | `context.Settings.Id` |
| `operation` | `context.Request.Operation.ToString()`: `ReadHoldingRegisters`, `ReadInputRegisters`, `WriteSingleRegister`, `WriteMultipleRegisters` |
| `result` | `"success"` или `"error"` (по итоговому `context.Response`) |

Важные свойства:

- метрика считается **на операцию**, а не на попытку: `MetricsMiddleware` стоит снаружи
  `RetryMiddleware`, поэтому три неудачные попытки дадут одну запись `result=error`;
- в теге `result` **нет** `ErrorKind` — причину ошибки нужно смотреть в логах
  (`LoggingMiddleware` пишет категорию и текст);
- инструменты статические и не освобождаются: метрики живут весь процесс.

### 5.2. Что нужно, чтобы метрики работали

1. `metrics` должен быть в `Pipeline`. По умолчанию он там есть
   (`timeout, logging, metrics, validation, retry, writeProtection`), но если вы задали
   собственный список и забыли `metrics` либо установили `"Pipeline": []` — метрик не
   будет.
2. Слушатель (`MeterListener`, OpenTelemetry-экспортёр, `dotnet-counters`) должен
   включить инструмент **до** того, как пошли измерения: подписка выполняется при старте
   процесса, до первых запросов.
3. Метрики привязаны к операции через пайплайн: если запрос отклонён
   `ValidationMiddleware` (в порядке по умолчанию он стоит ниже `metrics`), запись всё
   равно попадёт в счётчик с `result=error`; успешно коротко замкнутый запрос (например,
   кэшем, размещённым под `metrics`) — с `result=success`.

### 5.3. Полный пример `MeterListener`

Счётчик — `long`, гистограмма — `double`, поэтому нужны оба обработчика:

```csharp
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;

using MeterListener listener = new();

listener.InstrumentPublished = (instrument, l) =>
{
    if (instrument.Meter.Name == "Mvk.Modbus")
    {
        l.EnableMeasurementEvents(instrument);
    }
};

listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
{
    Console.WriteLine(
        $"{DateTime.Now:HH:mm:ss.fff}  {instrument.Name}: {value}  [{FormatTags(tags)}]");
});

listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
{
    Console.WriteLine(
        $"{DateTime.Now:HH:mm:ss.fff}  {instrument.Name}: {value:F1} мс  [{FormatTags(tags)}]");
});

listener.Start();

// ... дальше создаём пул и выполняем запросы ...

static string FormatTags(ReadOnlySpan<KeyValuePair<string, object?>> tags)
{
    return string.Join(", ", tags.ToArray().Select(tag => $"{tag.Key}={tag.Value}"));
}
```

Пример вывода:

```text
14:22:31.418  mvk.modbus.requests: 1  [device=mvk-1, operation=ReadHoldingRegisters, result=success]
14:22:31.420  mvk.modbus.duration: 12,4 мс  [device=mvk-1, operation=ReadHoldingRegisters, result=success]
14:22:32.905  mvk.modbus.requests: 1  [device=mvk-1, operation=ReadHoldingRegisters, result=error]
```

### 5.4. Экспорт в OpenTelemetry / Prometheus

Для полноценного экспорта достаточно указать meter `Mvk.Modbus`:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("Mvk.Modbus")
        .AddPrometheusExporter());
```

Быстрая проверка «жив ли счётчик» без настройки экспорта:

```powershell
dotnet-counters monitor --counters Mvk.Modbus -p <PID>
```

Пакеты `OpenTelemetry.*`/`Prometheus` в библиотеку не входят — это зависимости
приложения; сама библиотека ничего, кроме `System.Diagnostics.Metrics`, не требует.

---

## 6. Диагностические сценарии

### 6.1. «Устройство молчит»

Симптомы: запрос завершается `Timeout`, состояние устройства переходит в `Faulted`,
`LastError.ErrorKind == MvkErrorKind.Timeout`, в логе — `Warning` транспорта
«ошибка обращения» и `Warning` `LoggingMiddleware`.

Возможны два принципиально разных случая:

| Что происходит | Текст ошибки | На что смотреть |
|---|---|---|
| TCP-соединение не устанавливается (хост выключен, порт закрыт, фаервол) | `Таймаут подключения к 10.20.0.211:502 (3000 мс).` — истёк `ConnectTimeoutMs`; либо `Не удалось подключиться к 10.20.0.211:502: …` (сокетная ошибка) | Доступность хоста и порта: `Test-NetConnection 10.20.0.211 -Port 502`; кабель, коммутатор, питание |
| TCP-соединение есть, но ответа нет | `Таймаут при обращении к 10.20.0.211:502.` — истёк `ReadTimeoutMs` | Устройство занято/зависло; увеличьте `ReadTimeoutMs`; проверьте, не идёт ли параллельный опрос другого клиента |

Порядок диагностики:

1. `Test-NetConnection 10.20.0.211 -Port 502` — проверка связности до запуска приложения.
2. Проверьте `ReadTimeoutMs` (по умолчанию 1000 мс) и `ConnectTimeoutMs` (3000 мс);
   для медленного канала их можно поднять.
3. Воспроизведите ситуацию на эмуляторе: `SilentTcpServer` из `tests/Mvk.Testing`
   принимает соединение и не отвечает — это ровно сценарий «TCP есть, ответа нет»;
   `MvkTestServer.GetFreePort()` даёт закрытый порт — сценарий «TCP нет».
4. Посмотрите `device.State`/`device.LastError`: `Faulted` + `Timeout` подтверждают
   связь, а не данные.

```csharp
using Mvk.Testing;

await using SilentTcpServer silent = SilentTcpServer.Start();
// options.Defaults.Port = silent.Port; ReadTimeoutMs = 300
// Первый же запрос даст Timeout и переведёт устройство в Faulted.
```

### 6.2. «Ошибка при нечётном запросе»

Симптом: `DeviceError` с `ModbusExceptionCode == 0x01`:

```text
МВК 'mvk-1': ошибка обращения [10.20.0.211:502].
Устройство 'mvk-1' вернуло ошибку Modbus 0x01 (функция 0x83).
```

Причина: МВК01 принимает только запросы с чётным адресом и чётным числом регистров;
запрос ушёл на устройство, потому что локальная проверка выключена
(`RequireEvenAddressAndQuantity = false`).

Решение:

1. включить проверку: `RequireEvenAddressAndQuantity = true` (в JSON или через
   `AddMvk01Devices()`/`Mvk01Connection`);
2. читать параметры парами регистров: 32-битные значения занимают 2 регистра, поэтому
   корректные запросы всегда чётные (`0x1004 x2`, `0x2000 x86`);
3. если сомневаетесь в адресе — сверьтесь с `Mvk01RegisterMap` и
   `MVK01_registers.txt`.

После включения проверки ошибка становится локальной и мгновенной:

```text
Адрес 0x2001 и число регистров 1 должны быть чётными (особенность устройства 'mvk-1').
```

### 6.3. «Запись отклонена»

Ошибка приходит как `Validation` из `WriteProtectionMiddleware` — до выхода в сеть:

| Текст | Причина | Решение |
|---|---|---|
| `Устройство 'mvk-1' доступно только для чтения (WritePolicy=ReadOnly).` | `WritePolicy = ReadOnly` | Сменить политику (`AllowAll`/`ProtectConfigArea`) или отказаться от записи |
| `Запись в защищённую область конфигурации (0x0000–0x0FFF) устройства 'mvk-1' запрещена. Разрешите AllowConfigurationWrite.` | `WritePolicy = ProtectConfigArea` и `AllowConfigurationWrite = false`, запрос задел защищённую область | Разрешить запись явно: `Defaults.AllowConfigurationWrite = true` или `ConnectAsync(..., allowConfigurationWrite: true)`, либо писать вне защищённой области |
| `Для операции WriteMultipleRegisters отсутствуют данные записи.` | В `ModbusRequest` не переданы данные | Исправить запрос (обычный конструктор `ModbusRequest` такое не пропустит — защитная проверка middleware) |

Для МВК01 системные команды (`SaveConfiguration`, `Reboot`) пишут в регистр `0x0002`,
который лежит в защищённой области: если запись не разрешена явно, она будет
отклонена, и команда вернёт `Mvk01CommandStatus.TransportError` с этой ошибкой
в `Error`.

### 6.4. «После перезагрузки устройства всё восстановилось само»

Так и задумано: библиотека восстанавливает связь без кода приложения.

1. Любая ошибка запроса в `NModbusTransport` закрывает TCP-соединение
   (`CloseConnection()`); следующий запрос подключится лениво.
2. `MvkDevice.ApplyResult` временную ошибку (`IsTransient`) переводит устройство в
   `Faulted`, увеличивает счётчик подряд идущих сбоев `N` и планирует реконнект:
   задержка равна `ReconnectBaseDelayMs × 2^(N−1)` с ограничением `ReconnectMaxDelayMs`
   (по умолчанию 500 мс … 30000 мс).
3. Фоновый цикл пула (при `Pool.ReconnectEnabled = true`) каждые
   `HealthCheckIntervalSeconds` (по умолчанию 15 с) вызывает `MaintainAsync`; для
   `Faulted`-устройства с истёкшей задержкой выполняется `EnsureConnectedAsync`
   и подключение восстанавливается.
4. В лог пишется `Information`: `МВК 'mvk-1': соединение восстановлено [10.20.0.211:502].`

Что видно в журнале при перезапуске устройства:

```text
14:30:02.114  Warning  МВК 'mvk-1': ReadHoldingRegisters 0x1004 x2 — ошибка Transport за 2,1 мс (попытка 1): Ошибка при обращении к 10.20.0.211:502: …
14:30:02.115  Warning  МВК 'mvk-1': ошибка обращения [10.20.0.211:502].
14:30:05.121  Debug  МВК 'mvk-1': реконнект не удался — Transport: Не удалось подключиться к 10.20.0.211:502: …
14:30:18.402  Information  МВК 'mvk-1': соединение восстановлено [10.20.0.211:502].
```

Переход `Connected → Faulted` в этом сценарии приходит подписчикам через событие
`StateChanged` (`args.NewState == MvkDeviceState.Faulted`, `args.Error?.ErrorKind == MvkErrorKind.Transport`):
запись о нём в журнал добавляет приложение.

Обратите внимание: неудачные попытки реконнекта пишутся на уровне `Debug` — при
минимальном уровне `Information` вы увидите только начало сбоя и итоговое восстановление.
Если нужен журнал всех попыток, включите `Debug` для категории `Mvk.Modbus.Pool` или
подпишитесь на `StateChanged`.

Если `ReconnectEnabled = false`, фонового реконнекта нет: устройство остаётся в
`Faulted`/`Disconnected`, а связь восстановится лениво при следующем запросе (транспорт
подключается сам). Явный `MaintainAsync()` можно вызывать и вручную.

### 6.5. Как отличить ошибку данных от ошибки связи

| Признак | Ошибка связи | Ошибка данных/конфигурации |
|---|---|---|
| `ErrorKind` | `Transport`, `Timeout`, `Busy` | `DeviceError`, `Validation`, `DeviceNotFound`, `DeviceDisabled`, `Configuration` |
| `IsTransient` | `true` | `false` |
| `ModbusExceptionCode` | — | код Modbus (для `DeviceError`) |
| Уходит ли запрос на устройство | Может не уйти вовсе (подключение) или уйти без ответа | `DeviceError` — уходит и отклоняется устройством; `Validation` — не уходит |
| Состояние устройства | Обычно `Faulted` | Обычно остаётся `Connected`/`Disconnected` (сокет не страдает) |
| Что делать | Ждать реконнект, повторить | Исправить запрос/настройку, не повторять |

Практический приём: перед обработкой запомните `device.State` и `device.LastError`.
Сочетание «ошибка `DeviceError` + `State == Connected`» означает, что связь в порядке и
виноват запрос; сочетание «`Timeout` + `State == Faulted`» — что проблема в канале.

---

## 7. Практические примеры

### 7.1. Централизованный `RerunDeviceRequestAsync`

Метод-обёртка для прикладного кода: выполняет запрос, при временной ошибке ждёт
восстановления связи и повторяет один раз. Ошибки данных и отмена пробрасываются без
повторов.

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Mvk.Modbus;
using Mvk.Modbus.Pool;

/// <summary>
/// Выполняет запрос к устройству; временную ошибку пережидает и повторяет один раз.
/// </summary>
public static async Task<T> RerunDeviceRequestAsync<T>(
    IMvkDevice device,
    Func<CancellationToken, Task<T>> request,
    ILogger? logger = null,
    TimeSpan? reconnectWait = null,
    CancellationToken cancellationToken = default)
{
    TimeSpan wait = reconnectWait ?? TimeSpan.FromSeconds(10);

    try
    {
        return await request(cancellationToken).ConfigureAwait(false);
    }
    catch (MvkModbusException ex) when (ex.IsTransient)
    {
        logger?.LogWarning(
            "МВК '{DeviceId}': временная ошибка {ErrorKind} — {Message}. Ждём реконнект.",
            device.Settings.Id, ex.ErrorKind, ex.Message);

        await WaitForReconnectAsync(device, wait, cancellationToken).ConfigureAwait(false);

        // Если реконнект выключен, транспорт подключится лениво при повторе;
        // если устройство всё ещё недоступно — повторится временная ошибка.
        return await request(cancellationToken).ConfigureAwait(false);
    }
}

private static async Task WaitForReconnectAsync(
    IMvkDevice device,
    TimeSpan timeout,
    CancellationToken cancellationToken)
{
    DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;

    while (DateTimeOffset.UtcNow < deadline)
    {
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);

        if (device.State == MvkDeviceState.Connected)
        {
            return;
        }
    }
}
```

Использование с обёртками чтения:

```csharp
ushort[] registers = await RerunDeviceRequestAsync(
    device,
    ct => device.ReadHoldingRegistersAsync(0x1004, 2, ct),
    logger,
    cancellationToken: cts.Token);
```

Что здесь важно:

- `catch (MvkModbusException ex) when (ex.IsTransient)` — повторяются только
  `Transport`/`Timeout`/`Busy`; `DeviceError` и `Validation` уходят вызывающему коду
  сразу;
- отмена (`OperationCanceledException`) не перехватывается вообще — она проходит мимо
  `catch` и завершает цикл;
- `RetryMiddleware` уже сделал `MaxRetries + 1` попыток внутри одного запроса; этот
  метод добавляет ещё одну попытку **после** ожидания реконнекта — для длительных
  перерывов связи;
- для команд Save/Reboot метод применять не стоит: перед повтором нужно убедиться, что
  устройство действительно не выполнило команду.

### 7.2. Логирование ошибок в файл через Serilog

Полная конфигурация — в разделе 4.4. Ключевые моменты прикладного кода:

```csharp
builder.Logging.ClearProviders();
builder.Services.AddSerilog(Log.Logger, dispose: true);

// ...
try
{
    await device.ReadChannelAsync(1, cts.Token);
}
catch (MvkModbusException ex)
{
    // Serilog.Extensions.Logging сам развернёт ex.ToString() с InnerException.
    Log.Error(ex, "Чтение канала МВК '{DeviceId}' не выполнено: {ErrorKind}",
        device.Id, ex.ErrorKind);
    throw;
}
```

Такой вызов попадёт в файл одной записью с полной цепочкой исключений
(`MvkModbusException` → `SocketException`/`SlaveException`), категорией вызывающего кода
и меткой времени. Для сбоев связи уровень лучше оставить `Warning`, для ошибок
данных — `Error`, для отмены — не логировать.

### 7.3. Вывод метрик

Слушатель из раздела 5.3 можно оформить как вспомогательный класс и подключать одной
строкой:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;

/// <summary>
/// Печатает метрики библиотеки в консоль. Освобождать после остановки приложения.
/// </summary>
public sealed class MvkMetricsPrinter : IDisposable
{
    private readonly MeterListener listener = new();

    public MvkMetricsPrinter()
    {
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Mvk.Modbus")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>(OnLong);
        listener.SetMeasurementEventCallback<double>(OnDouble);
        listener.Start();
    }

    private static void OnLong(
        Instrument instrument, long value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        Console.WriteLine($"{instrument.Name}: {value}  [{FormatTags(tags)}]");
    }

    private static void OnDouble(
        Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        Console.WriteLine($"{instrument.Name}: {value:F1} мс  [{FormatTags(tags)}]");
    }

    private static string FormatTags(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        return string.Join(", ", tags.ToArray().Select(tag => $"{tag.Key}={tag.Value}"));
    }

    public void Dispose() => listener.Dispose();
}
```

Запускайте печать **до** первых запросов, иначе часть измерений не будет поймана:

```csharp
using MvkMetricsPrinter printer = new();
// ... создание пула и выполнение запросов ...
```

### 7.4. `LastError` и `State` для UI

`IMvkDevice` даёт готовые данные для индикатора состояния: `State`, `IsConnected`,
`LastError`, `LastRequestAt` и событие `StateChanged`. Во view-model WPF обновление
привязок нужно переносить в UI-поток:

```csharp
using System;
using System.ComponentModel;
using System.Threading;
using Mvk.Modbus;
using Mvk.Modbus.Pool;

public sealed class MvkDeviceViewModel : INotifyPropertyChanged
{
    private readonly IMvkDevice device;
    private readonly SynchronizationContext? uiContext;

    public MvkDeviceViewModel(IMvkDevice device)
    {
        this.device = device;
        uiContext = SynchronizationContext.Current;

        device.StateChanged += OnStateChanged;
        Apply(device.State, device.LastError);
    }

    public string Id => device.Settings.Id;
    public string Endpoint => device.Settings.Endpoint;
    public string StateText { get; private set; } = string.Empty;
    public string ErrorText { get; private set; } = "нет";
    public bool HasError { get; private set; }

    private void OnStateChanged(object? sender, MvkDeviceStateChangedEventArgs args)
    {
        void Update() => Apply(args.NewState, args.Error ?? device.LastError);

        if (uiContext is null)
        {
            Update();
        }
        else
        {
            uiContext.Post(_ => Update(), null);
        }
    }

    private void Apply(MvkDeviceState state, MvkModbusException? error)
    {
        StateText = state switch
        {
            MvkDeviceState.Connected => "на связи",
            MvkDeviceState.Connecting => "подключение…",
            MvkDeviceState.Faulted => "нет связи",
            _ => "отключено"
        };

        HasError = error is not null;
        ErrorText = error is null ? "нет" : $"{error.ErrorKind}: {error.Message}";

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StateText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ErrorText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasError)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
```

Нюанс: `StateChanged` возникает только при смене состояния. Если пришла
нетранзиентная ошибка (`DeviceError`) при живом сокете, состояние остаётся `Connected`,
`LastError` обновляется, но события не будет. Для индикатора «последняя ошибка»
обновляйте `LastError` периодически из цикла опроса или из таймера.

---

## 8. Таблица «сообщение → причина → решение»

Тексты приведены с подставленными значениями для устройства `mvk-1`
(`10.20.0.211:502`). Шаблоны — в исходниках `src/Mvk.Modbus` и `src/Mvk.MVK01`.

| Сообщение | Где | Причина | Решение |
|---|---|---|---|
| `Таймаут подключения к 10.20.0.211:502 (3000 мс).` | `NModbusTransport.ConnectAsync` | Не удалось установить TCP-соединение за `ConnectTimeoutMs` | Проверить `Test-NetConnection 10.20.0.211 -Port 502`, кабель, питание; при медленной сети увеличить `ConnectTimeoutMs` |
| `Не удалось подключиться к 10.20.0.211:502: <текст SocketException>` | `NModbusTransport.ConnectAsync` | Порт закрыт, хост недоступен, соединение отклонено | Проверить адрес/порт, что устройство включено и слушает 502 |
| `Таймаут при обращении к 10.20.0.211:502.` | `NModbusTransport.MapException` | TCP есть, но ответа за `ReadTimeoutMs`/`WriteTimeoutMs` не последовало | Увеличить таймаут; проверить занятость устройства и отсутствие параллельного опроса |
| `Ошибка при обращении к 10.20.0.211:502: <текст>` | `NModbusTransport.MapException` (фолбэк) | Разрыв (`ConnectionReset`), сбой ввода-вывода, нераспознанная ошибка | Смотреть `InnerException`; проверить сеть и стабильность питания устройства |
| `Устройство 'mvk-1' вернуло ошибку Modbus 0x01 (функция 0x83).` | `NModbusTransport.MapException` | Нечётный адрес/количество регистров (особенность МВК01) или неподдерживаемая функция | Включить `RequireEvenAddressAndQuantity`; читать парами регистров |
| `Устройство 'mvk-1' вернуло ошибку Modbus 0x02 (функция 0x83).` | `NModbusTransport.MapException` | `IllegalDataAddress`: адрес вне карты регистров | Сверить адрес с `Mvk01RegisterMap` / `MVK01_registers.txt` |
| `Устройство 'mvk-1' вернуло ошибку Modbus 0x03 (функция 0x90).` | `NModbusTransport.MapException` | `IllegalDataValue`: неверное количество/структура запроса при записи | Проверить `Quantity` и `WriteData` |
| `Адрес 0x2001 и число регистров 1 должны быть чётными (особенность устройства 'mvk-1').` | `ValidationMiddleware` | Локальная проверка чётности при `RequireEvenAddressAndQuantity = true` | Сделать адрес и количество чётными |
| `Устройство 'mvk-1' доступно только для чтения (WritePolicy=ReadOnly).` | `WriteProtectionMiddleware` | `WritePolicy = ReadOnly` | Сменить политику, если запись действительно нужна |
| `Запись в защищённую область конфигурации (0x0000–0x0FFF) устройства 'mvk-1' запрещена. Разрешите AllowConfigurationWrite.` | `WriteProtectionMiddleware` | Запись в защищённую область при `ProtectConfigArea` и `AllowConfigurationWrite = false` | Включить `AllowConfigurationWrite` явно (`Defaults` или `ConnectAsync`) или писать вне области |
| `Таймаут операции WriteMultipleRegisters (0x0002) — превышено 3000 мс.` | `TimeoutMiddleware` | Операция не уложилась в бюджет `Items[MvkContextItems.TimeoutMs]` | Увеличить таймаут `ExecuteAsync(request, timeout, ct)`; для `SaveConfiguration` минимум 3 с |
| `Устройство 'mvk-1' не найдено в конфигурации пула.` | `MvkDevicePool.GetDevice` | Идентификатора нет в `Devices` | Использовать `TryGetDevice`, `AddDevice` или правильный `Id` |
| `Устройство 'mvk-1' отключено в конфигурации (Enabled = false).` | `MvkDevice.ExecuteCoreAsync` / `EnsureConnectedAsync` | Устройство выключено в конфигурации | Включить (`Enabled = true`) или не опрашивать |
| `Не задан идентификатор Id добавляемого устройства.` | `MvkDevicePool.AddDevice` | Пустой `Id` в `MvkDeviceOptions` | Заполнить `Id` |
| `Устройство 'mvk-1' уже существует в пуле.` | `MvkDevicePool.AddCore` | Дубликат `Id` при `AddDevice` | Использовать существующее устройство или другое имя |
| `Файл конфигурации не найден: '<путь>'.` | `MvkModbusOptionsLoader` | Нет JSON-файла по указанному пути | Проверить путь и `CopyToOutputDirectory` |
| `Секция 'MvkModbus' не найдена в конфигурации.` | `MvkModbusOptionsLoader` | В файле нет секции `MvkModbus` | Добавить секцию или указать правильное имя |
| `Некорректная конфигурация 'MvkModbus': <список нарушений>` | `MvkModbusOptionsLoader` | Не прошла `MvkModbusOptionsValidator` (дубли `Id`, пустой `Host`, диапазоны) | Исправить перечисленные поля; в DI при `ValidateOnStart` вместо этого — `OptionsValidationException` |
| `Middleware 'audit' не найден: нет встроенного имени и нет регистрации через AddModbusMiddleware<T>(services, "имя").` | `MvkMiddlewareResolver` | Имя в `Pipeline` не зарегистрировано | Исправить список `Pipeline` или зарегистрировать middleware |
| `Тип 'MyMiddleware' не реализует IModbusMiddleware.` | `MvkMiddlewareRegistry.Add` | `AddModbusMiddleware<T>` вызван для типа без интерфейса | Реализовать `IModbusMiddleware` |
| `Пайплайн завершился без ответа: терминальный обработчик не был вызван.` | `MvkPipeline.ExecuteAsync` | Свой middleware не вызвал `next` и не заполнил `context.Response` | Исправить middleware: короткое замыкание обязано сформировать ответ |
| `Номер канала должен быть в диапазоне 1..8.` | `Mvk01Device.ValidateChannel` | Передан номер вне 1–8 | Исправить аргумент (обычно `ArgumentOutOfRangeException`) |

---

## Подводные камни

1. **`DeviceError` не лечится реконнектом.** Это ответ устройства на неверный запрос;
   `IsTransient == false`, `RetryMiddleware` его не повторяет. Меняйте запрос, а не
   настройки связи.
2. **«Функция 0x83» в тексте `DeviceError` — это нормально.** `NModbus` кладёт в
   `FunctionCode` код из ответа-исключения старшим битом `0x80`; код самой ошибки
   смотрите в `ModbusExceptionCode`.
3. **`ExecuteAsync` не бросает — проверяйте `IsSuccess`.** Забытая проверка превращает
   ошибку в работу с пустым `Data`.
4. **`ModbusExceptionCode` заполнен не всегда.** Он есть только у `DeviceError`; для
   `Transport`/`Timeout`/`Validation` код всегда `null`.
5. **Отмена — это `OperationCanceledException`, а не `MvkErrorKind.Canceled`.** Не
   перехватывайте её «широким» `catch (Exception)` и не логируйте как сбой.
6. **`TimeoutMiddleware` возвращает ответ, а не исключение.** Если бюджет операции
   истёк, обёртки бросят `MvkModbusException(Timeout)`, а `ExecuteAsync` — вернёт
   `Failure`; залог правильной обработки — единый признак `IsTransient`.
7. **`EnsureConnectedAsync` бросает даже для выключенного устройства** (`DeviceDisabled`),
   тогда как `ExecuteAsync` в этой же ситуации просто возвращает `Failure`.
8. **Ошибки системных команд МВК01 не бросаются.** Проверяйте `result.IsSuccess` и
   `result.Status`; `TransportError`/`Timeout`/`BadCommand`/`FlashError` приходят внутри
   результата.
9. **Успешные запросы логируются на `Debug`.** При минимальном уровне `Information`
   вы видите только ошибки — это ожидаемое поведение, а не поломка логирования.
10. **Неудачные реконнекты — тоже `Debug`.** Если канал «мигает», включите `Debug` для
    `Mvk.Modbus.Pool` или подпишитесь на `StateChanged`, иначе в журнале останутся только
    первый сбой и итоговое восстановление.
11. **Одна неудача даёт две записи `Warning`** — транспорт и `LoggingMiddleware`.
    Дублирование не ошибка; если мешает, настройте фильтры по категориям.
12. **Метрика считается на операцию, не на попытку**, и не содержит `ErrorKind`.
    Разбирать причины нужно по логам, сверяя время и устройства.
13. **Метрики молчат без `metrics` в `Pipeline`** и без слушателя, включившего инструмент
    до первых измерений. Инструменты статические — они не освобождаются вместе с пулом.
14. **`LastError` может обновиться без события `StateChanged`.** Нетранзиентная ошибка при
    живом сокете не меняет состояние; для UI обновляйте `LastError` дополнительно.
15. **`ObjectDisposedException`, `ArgumentException`, `ArgumentOutOfRangeException` — не
    ошибки связи.** Это ошибки кода/жизненного цикла; в модели `MvkErrorKind` их нет.
16. **Записи и команды Save/Reboot не повторяйте автоматически.** При таймауте команда
    могла выполниться; сначала прочитайте состояние/статус устройства.

---

## См. также

- [Транспорт](04-transport-and-raw-registers.md) — `NModbusTransport`, таймауты, разрыв
  соединения, `ModbusExceptionCode` и особенность МВК01 с чётными адресами.
- [Пайплайн](06-pipeline-middleware.md) — `LoggingMiddleware`, `MetricsMiddleware`,
  `RetryMiddleware`, `TimeoutMiddleware`, порядок middleware и отмена.
- [FAQ](13-troubleshooting-faq.md) — частые вопросы и быстрые рецепты решения проблем.
- [README](../README.md) — сводка по ошибкам, логированию и метрикам, быстрый старт.
