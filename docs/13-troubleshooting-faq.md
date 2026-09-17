# Глава 13. Диагностика проблем и частые вопросы

Это практический справочник по типовым сбоям: от «устройство не отвечает» до
«значения выглядят мусором». Все сообщения об ошибках процитированы дословно
из исходников и тестов (`src/Mvk.Modbus/**`, `src/Mvk.MVK01/**`,
`tests/Mvk.Modbus.Tests/**`), а порядок проверок соответствует реальной цепочке
вызовов: сокет → конфигурация → пул → пайплайн → запрос → декодирование данных.

Перед чтением главы стоит помнить три вещи:

- ошибки связи (`Transport`, `Timeout`) — временные, библиотека повторяет запрос
  и восстанавливает соединение сама, прикладному коду достаточно продолжать опрос;
- ошибки данных (`DeviceError`, `Validation`, `Configuration`) — постоянные,
  нужно исправлять запрос или настройки;
- отмена вызывающим кодом (`CancellationToken`) — это не `MvkModbusException`,
  а `OperationCanceledException`.

Содержание:

1. [Быстрая диагностика «не работает»](#1-быстрая-диагностика-не-работает)
2. [Сообщения исключений: причина и решение](#2-сообщения-исключений-причина-и-решение)
3. [Разбор типовых симптомов](#3-разбор-типовых-симптомов)
4. [FAQ](#4-faq)
5. [Если ничего не помогло](#5-если-ничего-не-помогло)
6. [См. также](#см-также)

---

## 1. Быстрая диагностика «не работает»

Проверяйте шаги строго по порядку. Если «упало всё», почти всегда виноват первый
шаг (связь) или второй (конфигурация), а не код опроса.

### 1.1. Чек-лист

| № | Шаг | Что проверить | Признак проблемы | Действие |
|---|---|---|---|---|
| 1 | Связь | IP, порт 502, пинг, `Test-NetConnection`, файрвол | `Timeout`, `Transport` | Восстановить сеть, открыть порт, проверить адрес |
| 2 | Конфигурация | файл найден, секция `MvkModbus`, `Devices`, диапазоны | `Configuration`, `OptionsValidationException` | Исправить JSON/код, см. раздел 2 |
| 3 | Пул | устройство есть в `Devices`, `Enabled`, правильный `Id` | `DeviceNotFound`, `DeviceDisabled` | `GetDevice`/`TryGetDevice`, включить устройство |
| 4 | Пайплайн | имена middleware из `Pipeline` существуют, порядок корректен | `Middleware 'x' не найден` | Зарегистрировать middleware или убрать имя |
| 5 | Запрос | адрес по карте, чётность, количество ≤ 125, политика записи | `Validation`, `DeviceError 0x01/0x02` | Исправить адрес/количество/политику |
| 6 | Данные | `ByteOrder`, номер канала/параметра, момент обновления DSP | мусор или «замершие» значения | Сверить порядок байт и адреса, перечитать снимок |

Минимальный «быстрый» тест на уровне сети:

```powershell
Test-NetConnection 10.20.0.211 -Port 502
```

Если `TcpTestSucceeded: False` — дальше идти нет смысла: проблема не в библиотеке.

### 1.2. Минимальный диагностический дамп

Первое, что стоит вывести при любом сбое — состояние устройства и последнюю ошибку:

```csharp
using Mvk.Modbus;
using Mvk.Modbus.Pool;

static void PrintDeviceState(IMvkDevice device)
{
    Console.WriteLine($"Id         : {device.Settings.Id}");
    Console.WriteLine($"Endpoint   : {device.Settings.Endpoint}");
    Console.WriteLine($"SlaveId    : {device.Settings.SlaveId}, ByteOrder: {device.Settings.ByteOrder}");
    Console.WriteLine($"Enabled    : {device.Settings.Enabled}");
    Console.WriteLine($"State      : {device.State}, IsConnected: {device.IsConnected}");
    Console.WriteLine($"LastRequest: {device.LastRequestAt:O}");

    if (device.LastError is { } error)
    {
        Console.WriteLine($"LastError  : {error.ErrorKind} — {error.Message}");

        if (error.ModbusExceptionCode is { } code)
        {
            Console.WriteLine($"Modbus code: 0x{code:X2}");
        }
    }
    else
    {
        Console.WriteLine("LastError  : нет");
    }
}
```

Обратите внимание: `IsConnected` — состояние сокета, а не гарантия связи.
После любой ошибки транспорта сокет закрывается, поэтому `IsConnected == false`
даже если `State` ещё `Connected` (нетранзиентная ошибка состояние не меняет).

### 1.3. Таблица «симптом → вероятная причина → действие»

| Симптом | Вероятная причина | Действие |
|---|---|---|
| `Timeout` при каждом запросе | не тот IP/порт, устройство выключено, чужой `SlaveId` | сверить `Host`/`Port`/`SlaveId`, `Test-NetConnection` |
| `Transport` «Не удалось подключиться» | порт закрыт, отказ TCP, сброс сокета | файрвол, маршрутизация, занятый порт |
| Первые запросы успешны, потом сплошной `Timeout` | устройство перезагрузилось/пропала сеть | ничего: реконнект идёт в фоне, см. 3.6 |
| `Устройство ... вернуло ошибку Modbus 0x01` | нечётный адрес или число регистров | читать чётными парами, включить `RequireEvenAddressAndQuantity` |
| `Устройство ... вернуло ошибку Modbus 0x02` | адрес вне карты регистров | сверить адрес с `Mvk01RegisterMap` |
| Запись завершается `Validation` | `WritePolicy=ReadOnly` или защищённая область | `AllowConfigurationWrite`, см. 3.5 |
| `SaveConfiguration` возвращает `Timeout` | Flash не успел за 3 с | повторить/увеличить таймаут, см. 3.4 |
| Значения не меняются | DSP-буфер, постоянный режим, неверный адрес | см. 3.2 |
| Значения выглядят мусором (`1.4E-38`, `NaN`) | неверный `ByteOrder` | сверить регистр `0x000C`, см. 3.2 |
| `Middleware 'x' не найден` | имя в `Pipeline` не зарегистрировано | `AddModbusMiddleware<T>("x")` или исправить имя |
| Пустой пул при старте | секция `MvkModbus` не найдена/не привязана | см. 2.1 и FAQ 4.3 |
| `OptionsValidationException` при старте | нарушения валидатора | исправить перечисленные поля |
| `OperationCanceledException` при Ctrl+C | отмена токена «вытекает» наружу | ловить `OperationCanceledException`, см. 3.7 |
| Приложение «зависло» при закрытии | синхронный `Dispose()` пула | использовать `await using`/`DisposeAsync` |

---

## 2. Сообщения исключений: причина и решение

Библиотека использует четыре вида ошибок:

- `MvkModbusException` (категория `ErrorKind`, `DeviceId`, `ModbusExceptionCode`) —
  основной тип для ошибок конфигурации, пула, транспорта и данных;
- `OptionsValidationException` — только в DI (`AddMvkModbus` + `ValidateOnStart`),
  внутри — список нарушений валидатора;
- `ArgumentException`/`ArgumentOutOfRangeException` — ошибки вызова
  (неверные аргументы методов), это ошибки программиста, а не связи;
- `OperationCanceledException` — отмена токеном, не ошибка устройства.

В таблицах ниже заполнители заменены примерами: `{fullPath}` → реальный путь,
`{id}` → `mvk-1`, `{sectionName}` → `MvkModbus`, `N` → конкретные числа.

### 2.1. Конфигурация, DI и пул

| Сообщение исключения | Категория/тип | Причина | Решение |
|---|---|---|---|
| `Файл конфигурации не найден: 'C:\app\mvkmodbus.json'.` | `MvkModbusException`, `Configuration` | Файла нет по указанному пути | Проверить путь; в проекте — `CopyToOutputDirectory` для `mvkmodbus.json` |
| `Не задан путь к файлу конфигурации.` | `ArgumentException` | Пустой путь в `BuildConfiguration`/`LoadFromJsonFile` | Передать реальный путь к файлу |
| `Секция 'MvkModbus' не найдена в конфигурации.` | `MvkModbusException`, `Configuration` | В JSON нет секции верхнего уровня `MvkModbus` | Добавить секцию; проверить написание (автономный загрузчик) |
| `Не задано имя секции конфигурации.` | `ArgumentException` | Пустой `sectionName` | Не передавать параметр либо задать имя |
| `Некорректная конфигурация 'MvkModbus': <список нарушений>.` | `MvkModbusException`, `Configuration` | Валидатор нашёл нарушения при загрузке файла | Исправить перечисленные поля (строки ниже) |
| `Секция конфигурации MvkModbus не задана.` | нарушение валидатора | В `Validate` передан `null` вместо секции | Исправить биндинг/регистрацию |
| `В списке устройств найден пустой элемент.` | нарушение валидатора | `null` в массиве `Devices` (лишняя запятая) | Убрать пустой элемент |
| `У устройства не задан обязательный идентификатор Id.` | нарушение валидатора | Пустой или пробельный `Id` | Задать уникальный `Id` |
| `Устройство 'mvk-1': идентификатор Id не уникален.` | нарушение валидатора | Два устройства с одинаковым `Id` (регистр не важен) | Переименовать или удалить дубликат |
| `Устройство 'mvk-1': не задан адрес Host.` | нарушение валидатора | `Host` пустой | Указать IP или DNS-имя |
| `Defaults.Port = 70000 вне допустимого диапазона 1..65535.` | нарушение валидатора | Порт вне диапазона | Исправить `Defaults.Port` |
| `Defaults.SlaveId = 0 вне допустимого диапазона 1..247.` | нарушение валидатора | `SlaveId` вне диапазона | Исправить (типовое значение для МВК01 — 1) |
| `Устройство 'mvk-1'.ReadTimeoutMs = 0 вне допустимого диапазона 1..600000.` | нарушение валидатора | Таймауты задаются только в `Defaults` и дублируются в проверке устройства | Исправить `Defaults.ReadTimeoutMs` |
| `Defaults.ReconnectMaxDelayMs не может быть меньше Defaults.ReconnectBaseDelayMs.` | нарушение валидатора | Границы backoff перепутаны | Выровнять значения |
| `Defaults.ProtectedWriteEndAddress не может быть меньше Defaults.ProtectedWriteStartAddress.` | нарушение валидатора | Начало/конец защищённой области перепутаны | Исправить границы |
| `OptionsValidationException` при старте хоста | DI-исключение | Любое из нарушений выше в DI-сценарии | Смотреть `Failures` исключения, исправить конфигурацию |
| `Middleware 'audit' не найден: нет встроенного имени и нет регистрации через AddModbusMiddleware<T>(services, "имя").` | `MvkModbusException`, `Configuration` | Имя в `Pipeline` не совпало со встроенным и не зарегистрировано | Зарегистрировать `AddModbusMiddleware<MyMiddleware>("audit")` или убрать из `Pipeline` |
| `Устройство 'mvk-9' не найдено в конфигурации пула.` | `MvkModbusException`, `DeviceNotFound` | `GetDevice` с неизвестным `Id` | Использовать `TryGetDevice`, верный `Id` или `AddDevice` |
| `Не задан идентификатор устройства.` | `ArgumentException` | Пустая строка/null в `GetDevice`/`TryGetDevice` | Передавать непустой `Id` |
| `Не задан идентификатор Id добавляемого устройства.` | `MvkModbusException`, `Configuration` | `AddDevice` без `Id` | Задать `Id` в `MvkDeviceOptions` |
| `Устройство 'mvk-1' уже существует в пуле.` | `MvkModbusException`, `Configuration` | `AddDevice` с существующим `Id` | Использовать другой `Id` |
| `Устройство 'mvk-1' отключено в конфигурации (Enabled = false).` | `MvkModbusException`, `DeviceDisabled` | Устройство выключено в конфигурации | Включить (`Enabled: true`) или не опрашивать его |

### 2.2. Транспорт, пайплайн, запросы и кодек

| Сообщение исключения | Категория/тип | Причина | Решение |
|---|---|---|---|
| `Таймаут подключения к 10.20.0.211:502 (3000 мс).` | `Timeout` | TCP-соединение не установилось за `ConnectTimeoutMs` | Сеть/порт/файрвол; увеличить `ConnectTimeoutMs` |
| `Не удалось подключиться к 10.20.0.211:502: <текст ошибки>` | `Transport` | Отказ в соединении, недоступный хост, ошибка DNS | Сверить `Host`/`Port`, проверить доступность |
| `Таймаут при обращении к 10.20.0.211:502.` | `Timeout` | Устройство не ответило за `ReadTimeoutMs`/`WriteTimeoutMs` | Проверить `SlaveId`, нагрузку; увеличить таймаут |
| `Ошибка при обращении к 10.20.0.211:502: <текст ошибки>` | `Transport` | Разрыв, сброс сокета, ошибка ввода-вывода | Устройство/сеть; реконнект выполняется автоматически |
| `Устройство 'mvk-1' вернуло ошибку Modbus 0x01 (функция 0x83).` | `DeviceError`, код `0x01` | Нечётный адрес/количество регистров (особенность МВК01) | Читать чётными парами; включить `RequireEvenAddressAndQuantity` |
| `Устройство 'mvk-1' вернуло ошибку Modbus 0x02 (функция 0x83).` | `DeviceError`, код `0x02` | Адрес вне карты регистров | Сверить адрес с `Mvk01RegisterMap` |
| `Устройство 'mvk-1' вернуло ошибку Modbus 0x<код> (функция 0x<функция>).` | `DeviceError` | Любой другой код от устройства | Разбирать по документации МВК01; код доступен в `ModbusExceptionCode` |
| `Таймаут операции ReadHoldingRegisters (0x1004) — превышено 1000 мс.` | `Timeout` | Истёк общий таймаут операции (`ExecuteAsync(request, timeout)`) | Увеличить переданный таймаут; помнить, что в него входят и повторы |
| `Адрес 0x2001 и число регистров 1 должны быть чётными (особенность устройства 'mvk-1').` | `Validation` | `RequireEvenAddressAndQuantity = true`, запрос нечётный | Выровнять адрес и количество до чётных |
| `Устройство 'mvk-1' доступно только для чтения (WritePolicy=ReadOnly).` | `Validation` | Политика записи запрещает запись | Сменить `WritePolicy`, если запись действительно нужна |
| `Запись в защищённую область конфигурации (0x0000–0x0FFF) устройства 'mvk-1' запрещена. Разрешите AllowConfigurationWrite.` | `Validation` | `ProtectConfigArea` + `AllowConfigurationWrite = false` | Разрешить запись или сузить защищённую область |
| `Для операции WriteMultipleRegisters отсутствуют данные записи.` | `Validation` | Ручной запрос-запись без `WriteData` | Заполнить данные записи |
| `Операция WriteSingleRegister не поддерживается транспортом.` | `Validation` | Неизвестная операция (ручная сборка запроса) | Использовать поддерживаемые операции 0x03/0x04/0x06/0x10 |
| `Пайплайн завершился без ответа: терминальный обработчик не был вызван.` | `Unknown` | Свой middleware коротко замкнул цепочку, не записав `Response` | Middleware обязан установить `context.Response` |
| `Строка занимает 10 байт и не помещается в 2 регистров (4 байт).` | `ArgumentException` (`RegisterCodec.FromText`) | Текст длиннее отведённого буфера | Увеличить `registerCount` или сократить строку |
| `Строка занимает 10 байт и не помещается в 2 регистров.` | `ArgumentException` (`Mvk01Codec.EncodeTextSwapped`) | То же для swap-строк канала | То же |
| `Номер канала должен быть в диапазоне 1..8.` | `ArgumentOutOfRangeException` | Номер канала вне 1–8 | Исправить номер (в МВК01 8 каналов АЦП) |
| `Номер тахометра должен быть в диапазоне 1..2.` | `ArgumentOutOfRangeException` | Номер тахометра вне 1–2 | Исправить номер |
| `Номер параметра должен быть в диапазоне 1..184.` | `ArgumentOutOfRangeException` | Номер параметра вне 1–184 | Использовать числовой номер из диапазона |
| `Для 32-битного значения требуется минимум два регистра.` | `ArgumentException` | В `RegisterCodec.ToSingle`/`ToUInt32` передан 1 регистр | Читать значения парами регистров |
| `Неизвестный порядок байт.` | `ArgumentOutOfRangeException` | Некорректное `MvkByteOrder` | Использовать `BigEndian`, `LittleEndian` или `Mixed` |
| `Число регистров должно быть от 1 до 125.` | `ArgumentOutOfRangeException` (`ModbusRequest`) | `quantity = 0` или больше 125 (чтение) | Дробить запрос на чанки ≤ 125 |
| `Число записываемых регистров должно быть от 1 до 123, получено 200.` | `ArgumentException` (`WriteMultipleRegisters`) | Слишком длинная запись | Дробить запись на чанки ≤ 123 |
| `Число значений записи (3) не совпадает с числом регистров (2).` | `ArgumentException` | `WriteData.Length != Quantity` | Согласовать данные и количество |
| `Диапазон адресов выходит за границы адресного пространства Modbus.` | `ArgumentOutOfRangeException` | `StartAddress + Quantity > 65536` | Проверить адрес и количество |
| `Требуется шесть регистров индикаторов DSP.` | `ArgumentException` (`DecodeDspIndicators`) | Передано меньше 6 регистров | Читать 6 регистров от `0x1020` |
| `Требуется 86 регистров для 43 значений float.` | `ArgumentException` (`DecodeFloats`) | Недостаточно регистров для спектра | Читать `Mvk01RegisterMap.SpectrumRegisterCount` = 86 |
| `Не задан адрес устройства.` | `ArgumentException` (`Mvk01Connection.ConnectAsync`) | Пустой `host` | Передать IP/DNS-имя |

Отдельно про `ErrorKind.Busy` и `ErrorKind.Canceled`: они объявлены в перечислении
и участвуют в `IsTransient`, но текущая реализация их не выбрасывает. Ожидание
очереди запросов — обычное ожидание `SemaphoreSlim`, а отмена вызывающим кодом
проявляется как `OperationCanceledException`.

### 2.3. Как читать сообщение об ошибке

`MvkModbusException` несёт не только текст, но и машинные атрибуты:

```csharp
try
{
    ushort[] data = await device.ReadHoldingRegistersAsync(0x1004, 2, cts.Token);
    Console.WriteLine($"ПО: {data[0]:X4} {data[1]:X4}");
}
catch (MvkModbusException ex)
{
    Console.WriteLine($"DeviceId          : {ex.DeviceId}");
    Console.WriteLine($"ErrorKind         : {ex.ErrorKind}");
    Console.WriteLine($"IsTransient       : {ex.IsTransient}");
    Console.WriteLine($"ModbusExceptionCode: 0x{ex.ModbusExceptionCode:X2}");
    Console.WriteLine($"Message           : {ex.Message}");
    Console.WriteLine($"InnerException    : {ex.InnerException?.Message}");
}
```

- `IsTransient == true` (`Transport`, `Timeout`, `Busy`) — связь; повтор и реконнект
  уже выполняются автоматически, код может продолжить цикл опроса;
- `IsTransient == false` (`DeviceError`, `Validation`, `Configuration`,
  `DeviceNotFound`, `DeviceDisabled`, `Unknown`) — постоянная ошибка, повторять
  бессмысленно без исправления запроса или настроек;
- `ModbusExceptionCode` заполнен только для ответов-исключений Modbus и здесь же
  находится исходный код устройства (например, `0x01`);
- `InnerException` хранит первопричину (например, `SocketException` или
  `TimeoutException` от NModbus).

---

## 3. Разбор типовых симптомов

### 3.1. Устройство недоступно

Порядок проверок:

1. `Test-NetConnection 10.20.0.211 -Port 502` — есть ли TCP-соединение вообще.
2. Порт: 502 по умолчанию, но в конфигурации может быть другой (`defaults.Port`,
   поле `Port` устройства); фактический порт устройства есть в регистре `0x000A`.
3. `SlaveId`: по умолчанию 1, допустимо 1–247; чужой `SlaveId` даёт таймаут,
   потому что устройство не отвечает.
4. Файрвол на ПК и в промышленной сети: TCP/502 должен быть разрешён в обе стороны.
5. Порт не занят другим Modbus-клиентом или сервисом: приложение «висит»
   на подключении, а устройство не отвечает на запросы.

Программная проверка с разбором категории ошибки:

```csharp
using Mvk.Modbus;

try
{
    await device.EnsureConnectedAsync(cts.Token);
    Console.WriteLine($"Связь есть: {device.Settings.Endpoint}");
}
catch (MvkModbusException ex) when (ex.ErrorKind == MvkErrorKind.Timeout)
{
    Console.WriteLine($"Устройство не ответило за {device.Settings.ConnectTimeoutMs} мс: {ex.Message}");
}
catch (MvkModbusException ex) when (ex.ErrorKind == MvkErrorKind.Transport)
{
    Console.WriteLine($"Сеть/сокет: {ex.Message}");
}
catch (MvkModbusException ex)
{
    Console.WriteLine($"{ex.ErrorKind}: {ex.Message}");
}
```

Как это выглядит в состоянии пула: после неудачного подключения устройство
переходит в `Faulted`, `LastError` содержит `Timeout` или `Transport`
(сообщение `Таймаут подключения к 10.20.0.211:502 (3000 мс).` либо
`Не удалось подключиться к 10.20.0.211:502: <текст>`), а фоновый цикл продолжает
попытки реконнекта с экспоненциальной задержкой.

### 3.2. Значения не меняются или выглядят мусором

Причины, в порядке вероятности:

1. **Значения действительно стабильны.** Если агрегат остановлен, СКЗ и спектр
   не меняются; проверьте `SensorVoltage` и `State` канала.
2. **DSP-буфер обновляется со своим периодом.** Библиотека не кэширует ответы:
   каждый вызов — реальный запрос к устройству. Но параметры считаются DSP
   и выкладываются в буферы с собственным темпом, поэтому слишком частые чтения
   могут показывать одно и то же значение, а сразу после включения флаги
   `Buffer16HzNotFilled`, `Buffer64HzNotFilled`, `Buffer512HzNotFilled` могут
   быть установлены. Перечитайте снимок через 1–2 с.
3. **Неверный адрес.** Классический случай — параметр N=48: в
   `MVK01_registers.txt` указан адрес `0x41F0`, фактически он равен `0x42F0`
   (формула `0x4000 + (N−1)×16`). Чтение по ошибочному адресу даёт нули или
   чужие значения.
4. **Неверный `ByteOrder`.** Прочитайте регистр `0x000C` и сверьтесь:
   `0x00000000` — `BigEndian`, `0x66666666` — `LittleEndian`,
   `0x99999999` — `Mixed` (см. README, раздел «Сырые регистры и кодеки»).
   При несовпадении float-значения выглядят как `1.4E-38`, `NaN` или огромные
   числа.

```csharp
ushort[] first = await raw.ReadHoldingRegistersAsync(0x000C, 2);
Console.WriteLine($"0x000C: 0x{RegisterCodec.ToUInt32(first, raw.Settings.ByteOrder):X8} " +
                  $"(ожидается 0x00000000 для BigEndian)");
```

5. **Тип спектра.** Единицы спектра зависят от регистра `0x001A`:
   0 — ускорение, 1 — скорость, 2 — перемещение. Если значения «не такие»,
   проверьте этот регистр.

Убедиться, что данные живые:

```csharp
Mvk01Channel first = await device.ReadChannelAsync(1, cts.Token);
await Task.Delay(2000, cts.Token);
Mvk01Channel second = await device.ReadChannelAsync(1, cts.Token);

Console.WriteLine($"СКЗ 10…1000 Гц : {first.RmsSets[2].Acceleration:F4} -> {second.RmsSets[2].Acceleration:F4} м/с2");
Console.WriteLine($"Напряжение    : {first.SensorVoltage:F4} -> {second.SensorVoltage:F4} В");
Console.WriteLine($"Состояние канала: {second.State}");
```

### 3.3. Спектр и ошибка Modbus 0x01

Спектр МВК01 — 43 полосы по 2 регистра, то есть 86 регистров от смещения
`SpectrumOffset` (`0x0002`) относительно базы канала. Устройство принимает только
запросы с чётным адресом и чётным числом регистров; запрос 43 регистров (или
с нечётным адресом) приводит к ответу-исключению `0x01`.

Если `RequireEvenAddressAndQuantity = true` (это делают `AddMvk01Devices()`
и `Mvk01Connection.ConnectAsync`), до устройства дело не дойдёт — `validation`
вернёт сообщение:

```
Адрес 0x2003 и число регистров 43 должны быть чётными (особенность устройства 'mvk-1').
```

Если проверка отключена, ответит устройство, и транспорт вернёт:

```
Устройство 'mvk-1' вернуло ошибку Modbus 0x01 (функция 0x83).
```

Правильное чтение спектра сырыми регистрами:

```csharp
const int channel = 1;

ushort baseAddress = (ushort)(Mvk01RegisterMap.ChannelData(channel)
    + Mvk01RegisterMap.SpectrumOffset);                       // 0x2002

ushort[] registers = await raw.ReadHoldingRegistersAsync(
    baseAddress,
    Mvk01RegisterMap.SpectrumRegisterCount,                   // 86
    cts.Token);

float[] spectrum = Mvk01Codec.DecodeFloats(
    registers,
    Mvk01RegisterMap.SpectrumBands,                           // 43
    raw.Settings.ByteOrder);
```

Адрес `0x2002` чётный, количество 86 чётное, 86 ≤ 125 — ограничения Modbus
соблюдены. Тот же результат проще получить через `ReadChannelAsync(1)`.

### 3.4. Команда Save возвращает Timeout

`SaveConfigurationAsync` выполняется в два этапа: запись команды по адресу
`0x0002` (2 регистра), затем опрос статуса каждые 200 мс до успеха, ошибки или
истечения общего таймаута (по умолчанию 3 с). Итог:

- `Mvk01CommandStatus.Success` — устройство вернуло `0x00000`;
- `Mvk01CommandStatus.BadCommand` — `0x00BAD` (неверная команда);
- `Mvk01CommandStatus.FlashError` — `0xF0BAD` (ошибка Flash, не сохранено);
- `Mvk01CommandStatus.Timeout` — статус не был получен вовремя;
- `Mvk01CommandStatus.TransportError` — ошибка при записи команды или
  нетранзиентная ошибка при чтении статуса (`Error` заполнен).

`Timeout` не всегда означает, что сохранение не началось: команда могла быть
принята, а статус просто не успел обновиться. Проверьте статус напрямую:

```csharp
Mvk01CommandResult result = await device.ExecuteSystemCommandAsync(
    Mvk01SystemCommand.SaveConfiguration,
    TimeSpan.FromSeconds(10));       // увеличенный таймаут вместо 3 с

Console.WriteLine(result);           // «Команда SaveConfiguration: Timeout»

if (result.Status == Mvk01CommandStatus.Timeout)
{
    ushort[] statusRegisters = await raw.ReadHoldingRegistersAsync(
        Mvk01RegisterMap.SystemCommand, 2, cts.Token);

    uint status = RegisterCodec.ToUInt32(statusRegisters, raw.Settings.ByteOrder);
    Console.WriteLine($"Статус команды в устройстве: 0x{status:X5}");
}
```

Если через несколько секунд статус равен `0x00000` — конфигурация сохранена,
`Timeout` был ложным (устройство медленно отвечало). Если `0xF0BAD` — ошибка
Flash, записывайте информацию для сервиса. Если статус не меняется —
повторите команду с бо́льшим таймаутом. На реальном устройстве не выполняйте
Save/Reboot без явного разрешения и учтите, что команда защищена политикой
записи (см. 3.5): без `AllowConfigurationWrite` вы получите `TransportError`
с `Error.ErrorKind = Validation`, а не `Timeout`.

### 3.5. Запись отклонена (WritePolicy / AllowConfigurationWrite)

Запись проверяет `WriteProtectionMiddleware`:

- `WritePolicy = ReadOnly` — любая запись отклоняется с сообщением
  `Устройство 'mvk-1' доступно только для чтения (WritePolicy=ReadOnly).`;
- `WritePolicy = ProtectConfigArea` (по умолчанию) и
  `AllowConfigurationWrite = false` — запись, пересекающая область
  `ProtectedWriteStartAddress..ProtectedWriteEndAddress` (по умолчанию
  `0x0000–0x0FFF`), отклоняется с сообщением
  `Запись в защищённую область конфигурации (0x0000–0x0FFF) устройства 'mvk-1' запрещена. Разрешите AllowConfigurationWrite.`

Область `0x0000–0x0FFF` включает регистр системных команд `0x0002`, конфигурацию
каналов `0x0100+` и конфигурацию тахометров `0x0900+`, поэтому под защиту попадают
и Save/Restore/Reboot.

Разрешить запись можно тремя способами:

```csharp
// 1. В DI — «обязательная» настройка типизированного слоя:
builder.Services.AddMvkModbus(builder.Configuration);
builder.Services.AddMvk01Devices();   // выставляет RequireEvenAddressAndQuantity и AllowConfigurationWrite
```

```json
// 2. В JSON — для конкретного устройства или всего пула:
{
  "MvkModbus": {
    "Defaults": { "AllowConfigurationWrite": true }
  }
}
```

```csharp
// 3. В коде — сузить защищённую область, если писать нужно только в её часть:
options.Defaults.ProtectedWriteStartAddress = 0x0100;
options.Defaults.ProtectedWriteEndAddress = 0x08FF;
```

Отличить «запись отклонил пайплайн» от «запись не прошла в транспорт»:

```csharp
ushort address = Mvk01RegisterMap.ChannelConfig(1);               // 0x0100
ushort[] values = RegisterCodec.FromSingle(1.25f, raw.Settings.ByteOrder); // SensorFactor

ModbusResponse response = await raw.ExecuteAsync(
    ModbusRequest.WriteMultipleRegisters(raw.Settings.Id, address, values), cts.Token);

if (!response.IsSuccess)
{
    // Validation — пайплайн; Transport/Timeout — связь; DeviceError — ответ устройства.
    Console.WriteLine($"{response.Error!.ErrorKind}: {response.Error.Message}");
}
```

Типовая ошибка при диагностике: `Mvk01CommandResult.Status == TransportError`
воспринимается как «нет связи», хотя на самом деле `result.Error.ErrorKind`
равен `Validation`, а внутри — сообщение о защищённой области.

### 3.6. После сбоя сети всё восстановилось само

Это штатная работа автореконнекта:

1. транспортная ошибка закрывает сокет; устройство переходит в `Faulted`,
   `LastError` заполняется, публикуется `StateChanged`/`DeviceStateChanged`;
2. задержка до следующей попытки считается по экспоненте:
   `ReconnectBaseDelayMs × 2^(n−1)` с ограничением `ReconnectMaxDelayMs`;
3. фоновый цикл обслуживания (при `Pool.ReconnectEnabled = true`, период
   `HealthCheckIntervalSeconds`, по умолчанию 15 с) вызывает
   `EnsureConnectedAsync`; при успехе состояние возвращается в `Connected`,
   `LastError` сбрасывается.

Как убедиться, что это реконнект, а не случайность:

```csharp
pool.DeviceStateChanged += (_, args) =>
    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  {args.Device.Settings.Id}: " +
                      $"{args.OldState} -> {args.NewState} ({args.Error?.Message ?? "без ошибки"})");
```

Характерные записи в логе:

| Уровень | Сообщение |
|---|---|
| `Information` | `МВК 'mvk-1': соединение восстановлено [10.20.0.211:502].` |
| `Debug` | `МВК 'mvk-1': реконнект не удался — Timeout: Таймаут подключения к 10.20.0.211:502 (3000 мс).` |
| `Debug` | `МВК 'mvk-1': соединение установлено [10.20.0.211:502], SlaveId 1.` |
| `Debug` | `МВК 'mvk-1': соединение закрыто после простоя (120 с).` |
| `Warning` | `МВК 'mvk-1': ошибка обращения [10.20.0.211:502].` |

Полезно знать: `Faulted` не «замораживает» устройство — следующий `ExecuteAsync`
сам пытается подключиться, не дожидаясь backoff. А нетранзиентные ошибки
(`DeviceError`, `Validation`) реконнект не запускают: если устройство отвечает,
восстанавливать нечего.

### 3.7. Приложение падает при Ctrl+C

Отмена вызывающим кодом — это `OperationCanceledException` (часто
`TaskCanceledException`), а не `MvkModbusException`. Если код ловит только
`MvkModbusException`, исключение отмены уходит наверх и (например, из `async Main`
без обработки) завершает процесс — внешне это выглядит как «падение при Ctrl+C».

Правильный шаблон:

```csharp
using CancellationTokenSource cts = new();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;      // не даём процессу завершиться мгновенно
    cts.Cancel();
};

try
{
    while (!cts.IsCancellationRequested)
    {
        float[] values = await device.ReadMeasurementByNumberAsync(
            Mvk01MeasurementNumber.Rms10To1000Acceleration, cts.Token);

        Console.WriteLine($"канал 1: {values[0]:F4} м/с2");
        await Task.Delay(500, cts.Token);
    }
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
    Console.WriteLine("Опрос остановлен пользователем.");
}
catch (MvkModbusException ex)
{
    Console.WriteLine($"Ошибка: {ex.ErrorKind} — {ex.Message}");
}
```

Дополнительные замечания:

- с `PeriodicTimer` используйте `while (await timer.WaitForNextTickAsync(cts.Token))`
  — это тот же `OperationCanceledException` при отмене;
- фильтр `when (cts.IsCancellationRequested)` защищает от того, что вы случайно
  «проглотите» чужую отмену (например, таймаут другой операции);
- таймаут операции — это не отмена: он приходит как `MvkModbusException`
  с `ErrorKind = Timeout`;
- при остановке освобождайте пул асинхронно: `await using MvkDevicePool pool = ...`
  или `await using Mvk01Connection mvk = ...`; синхронный `Dispose()` может
  заблокировать UI-поток;
- если отмена застала запись, результат записи неопределён (запрос мог уйти) —
  после возобновления перечитайте состояние устройства.

---

## 4. FAQ

### 4.1. Как подключиться к одному устройству?

Самый короткий путь — `Mvk01Connection`:

```csharp
using Mvk.MVK01;

await using Mvk01Connection mvk = await Mvk01Connection.ConnectAsync("10.20.0.211");
// порт 502, SlaveId 1, BigEndian, RequireEvenAddressAndQuantity, AllowConfigurationWrite

Mvk01DeviceInfo info = await mvk.ReadDeviceInfoAsync();
Mvk01Channel channel = await mvk.ReadChannelAsync(1);
```

`ConnectAsync` сразу проверяет связь и бросает `MvkModbusException`
(`Timeout`/`Transport`), если устройство недоступно. Если нужен опрос по номерам
параметров — используйте `mvk.Device.ReadMeasurementByNumberAsync(...)`: сам
`Mvk01Connection` этот метод не дублирует.

### 4.2. Как подключиться к нескольким устройствам?

Через пул: список `Devices` в JSON или в коде.

```json
{
  "MvkModbus": {
    "Devices": [
      { "Id": "mvk-1", "Host": "10.20.0.211" },
      { "Id": "mvk-2", "Host": "10.20.0.212" }
    ]
  }
}
```

```csharp
IMvkDevice first = pool.GetDevice("mvk-1");
IMvkDevice second = pool.GetDevice("mvk-2");

ushort[] a = await first.ReadHoldingRegistersAsync(0x1004, 2, cts.Token);
ushort[] b = await second.ReadHoldingRegistersAsync(0x1004, 2, cts.Token);
```

В DI то же самое доступно через `IMvk01DeviceFactory`:
`factory.Devices`, `factory.GetDevice("mvk-1")`, `factory.TryGetDevice(...)`.
Параллельный опрос разных устройств безопасен — у каждого свой транспорт,
сокет и очередь запросов.

### 4.3. Где хранить конфигурацию и как её загружать?

Три равноправных способа:

```csharp
// 1. Автономный загрузчик без DI.
MvkModbusOptions options = MvkModbusOptionsLoader.LoadFromJsonFile("mvkmodbus.json");
await using MvkDevicePool pool = new(options);

// либо сразу пул:
await using MvkDevicePool pool2 = MvkDevicePool.FromJsonFile("mvkmodbus.json");
```

```csharp
// 2. DI + файл (рекомендуется для хостов и служб).
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "mvkmodbus.json"),
    optional: false,
    reloadOnChange: false);

builder.Services.AddMvkModbus(builder.Configuration);
builder.Services.AddMvk01Devices();
```

3. Программно — `new MvkDevicePool(new MvkModbusOptions { ... })`.

Не забудьте скопировать `mvkmodbus.json` в выходной каталог:

```xml
<ItemGroup>
  <None Update="mvkmodbus.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

Важно: в DI отсутствие секции `MvkModbus` не является ошибкой — пул просто будет
пустым (`Devices = []`). Сообщение `Секция 'MvkModbus' не найдена в конфигурации.`
бросает только автономный загрузчик `MvkModbusOptionsLoader`. Изменение файла
на диске после создания пула ни на что не влияет: параметры уже развёрнуты в
`MvkDeviceSettings`, а middleware и интервал обслуживания зафиксированы при старте.

### 4.4. Как прочитать все 8 каналов одного параметра?

Одним запросом: `ReadMeasurementByNumberAsync` читает 16 регистров
(`0x4000 + (N−1)×16`) и возвращает `float[8]`.

```csharp
float[] rms = await device.ReadMeasurementByNumberAsync(
    Mvk01MeasurementNumber.Rms10To1000Acceleration, cts.Token);       // N = 55

Console.WriteLine($"Канал 1: {rms[0]:F4}, канал 8: {rms[7]:F4} м/с2");
```

Если параметр — битовое поле, используйте `ReadMeasurementRawByNumberAsync`
(например, `Mvk01MeasurementNumber.ChannelState` = 80).

### 4.5. Как прочитать спектр?

Полный снимок канала или только спектр:

```csharp
Mvk01Channel channel = await device.ReadChannelAsync(1, cts.Token);

for (int band = 1; band <= Mvk01SpectrumBands.Count; band++)
{
    float value = channel.GetSpectrumBand(band);
    if (value > 0.001f)
    {
        Console.WriteLine($"{Mvk01SpectrumBands.FormatBand(band),-10} {value:F4}");
    }
}
```

Сырыми регистрами: 86 регистров от `ChannelData(n) + 0x0002`, декодирование —
`Mvk01Codec.DecodeFloats(registers, 43, byteOrder)`, см. 3.3.

### 4.6. Как часто можно опрашивать устройство?

Период 0,5 с для МВК01 безопасен: запросы к одному устройству сериализуются,
данные рассчитываются DSP и выкладываются в буферы, повторная отправка
не требуется. Практические ориентиры:

- используйте `PeriodicTimer`, а не `Task.Delay` — первый не накапливает дрейф;
- не запускайте несколько параллельных циклов опроса одного устройства:
  `MaxConcurrentRequests = 1` (значение по умолчанию) и так выстраивает запросы
  в очередь;
- один запрос чтения — не более 125 регистров, поэтому `ReadMeasurementByNumberAsync`
  (16 регистров) дешевле, чем полный `ReadChannelAsync` (5 запросов);
- для отображения в UI выбирайте период по частоте обновления данных DSP,
  а не по минимально возможному времени ответа.

### 4.7. Можно ли работать с устройством из нескольких потоков?

Да. `MvkDevice` сериализует запросы через `SemaphoreSlim` (размер —
`MaxConcurrentRequests`), а пул хранит устройства в `ConcurrentDictionary`.
Потокобезопасность обеспечивается на уровне `IMvkDevice`/`IMvk01Device` — именно
эти интерфейсы и передавайте между потоками. Не делите между потоками вручную
созданные `NModbusTransport`/`MvkPipeline` без `MvkDevice`: транспорт
не потокобезопасен (см. примечания к главам 05 и 06). Для МВК01 оставляйте
`MaxConcurrentRequests = 1` — параллелизм меняет только порядок ожидания,
а не пропускную способность одного сокета.

### 4.8. Какие таймауты ставить?

Значения по умолчанию подходят для большинства задач:

| Параметр | По умолчанию | Назначение |
|---|---|---|
| `ConnectTimeoutMs` | 3000 | Установка TCP-соединения |
| `ReadTimeoutMs` | 1000 | Ожидание ответа на чтение |
| `WriteTimeoutMs` | 3000 | Ожидание ответа на запись |

Особенности:

- общий таймаут операции задаётся из кода: `ExecuteAsync(request, timeout, ct)`,
  `ExecuteSystemCommandAsync(command, timeout, ct)`; для Save/Reboot по умолчанию
  используется 3 с;
- `timeout` — самый внешний middleware, поэтому в общий бюджет входят все повторы
  (`MaxRetries + 1` попыток) и их задержки;
- таймауты устройства можно задать только через `Defaults`: поля таймаута
  в `MvkDeviceOptions` не переопределяются (в отличие от порта и `SlaveId`);
- для медленных операций увеличивайте не сокетный таймаут, а таймаут операции;
- рабочий пример в репозитории использует `ReadTimeoutMs = 2000`
  (`samples/Mvk.Sample.Console/mvkmodbus.json`).

### 4.9. Как записать настройки канала АЦП?

Только при явном понимании последствий и разрешении на запись. Порядок действий:

1. разрешить запись (`AddMvk01Devices()` или `AllowConfigurationWrite: true`);
2. сформировать 32-битные значения в порядке байт устройства
   (`RegisterCodec.FromSingle`/`FromUInt32`);
3. записать чётное число регистров по чётному адресу;
4. при необходимости сохранить во Flash командой `SaveConfiguration`.

```csharp
ushort address = (ushort)(Mvk01RegisterMap.ChannelConfig(1)
    + Mvk01RegisterMap.ChannelSensorFactorOffset);                 // 0x0102

ushort[] values = RegisterCodec.FromSingle(1.25f, raw.Settings.ByteOrder);
await raw.WriteMultipleRegistersAsync(address, values, cts.Token);

Mvk01CommandResult saved = await device.SaveConfigurationAsync(cts.Token);
Console.WriteLine(saved);
```

Перед записью прочитайте текущую конфигурацию (`ReadChannelConfigAsync`) и
сверьте смещения по главе 07a и `MVK01_registers.txt`.

### 4.10. Как проверить версию внутреннего ПО устройства?

```csharp
Mvk01DeviceInfo info = await mvk.ReadDeviceInfoAsync(cts.Token);
Console.WriteLine($"ПО: {info.FirmwareVersion}, серийный: {info.SerialNumber}");
```

Или сырыми регистрами:

```csharp
ushort[] version = await raw.ReadHoldingRegistersAsync(0x1004, 2, cts.Token);
Console.WriteLine(Mvk01Codec.DecodeFirmwareVersion(
    RegisterCodec.ToUInt32(version, raw.Settings.ByteOrder)));
```

Отличие от версии библиотеки: версия ПО (например, `1.7.20.0`) хранится
в устройстве; версия самой библиотеки определяется пакетом NuGet или тегом
репозитория, отдельного API для неё нет.

### 4.11. Почему пул не падает, если устройство выключено?

Потому что пул подключается лениво: при создании `MvkDevicePool` соединения
не устанавливаются, а конфигурация проверяется только на синтаксис (непустой
`Id`, `Host`, диапазоны). Первый запрос сам пытается подключиться; при неудаче
`ExecuteAsync` возвращает ответ с ошибкой, а удобные обёртки бросают
`MvkModbusException`. Приложение продолжает работать, а реконнект идёт в фоне.
Даже hosted-сервис `MvkModbusWarmupHostedService` при старте только логирует
состав пула (`Пул МВК инициализирован: устройств 1 (mvk-1).`) и никуда
не подключается.

### 4.12. Как отключить логирование, метрики и/или проверки?

Составом пайплайна в `Pipeline` (порядок — из конфигурации):

```json
{
  "MvkModbus": {
    "Pipeline": ["timeout", "validation", "retry", "writeProtection"]
  }
}
```

- без `logging` и `metrics` пропадут записи о запросах и метрики;
- пустой массив `"Pipeline": []` отключает всё, включая защиту записи и проверку
  чётности — так делать не рекомендуется;
- `null` или отсутствие ключа включает порядок по умолчанию
  (`timeout, logging, metrics, validation, retry, writeProtection`).

Настройка уровня логов — стандартная для `Microsoft.Extensions.Logging`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Mvk.Modbus": "Warning"
    }
  }
}
```

Успешные запросы `LoggingMiddleware` пишет на уровне `Debug`, ошибки — `Warning`.
Метрики публикуются только при наличии `metrics` в пайплайне (счётчик
`mvk.modbus.requests` и гистограмма `mvk.modbus.duration`).

### 4.13. Как добавить или убрать устройство на лету?

```csharp
IMvkDevice added = pool.AddDevice(new MvkDeviceOptions
{
    Id = "mvk-2",
    Host = "10.20.0.212"
});
// AddDevice применяет Defaults и не подключается сразу

// ...

bool removed = pool.RemoveDevice("mvk-2");
```

Ограничения: дубликат `Id` — `MvkModbusException` с сообщением
`Устройство 'mvk-2' уже существует в пуле.`; пустой `Id` —
`Не задан идентификатор Id добавляемого устройства.`; `Enabled` нельзя изменить
у существующего устройства — удаляйте и добавляйте заново; не удаляйте устройство
во время активных запросов к нему.

### 4.14. Обязательно ли использовать DI?

Нет. Три автономных входа: `Mvk01Connection.ConnectAsync` (одно устройство),
`MvkDevicePool.FromJsonFile`/`new MvkDevicePool(options)` (пул),
`new MvkPipelineBuilder()` (ручная сборка пайплайна с транспортом).
DI нужен только для конфигурации через `IConfiguration`, hosted-сервиса
и внедрения `ILogger<T>`/`IMvk01DeviceFactory`.

### 4.15. Можно ли использовать библиотеку в WPF/WinForms/ASP.NET?

Да, это обычная библиотека `net8.0` с async-first API. Рекомендации:

- не блокируйте UI-поток (`await`, без `.Result`/`.Wait()`);
- обновляйте привязки в UI-потоке (`Application.Current.Dispatcher.InvokeAsync`
  или `SynchronizationContext`);
- используйте `await using`/`DisposeAsync`, а не синхронный `Dispose()`;
- в ASP.NET Core вызывайте `AddMvkModbus` + `AddMvk01Devices`, при необходимости
  `AddMvkModbusHostedService`; пул — синглтон, один на процесс.

### 4.16. Как собрать библиотеку из исходников и запустить тесты?

```powershell
dotnet build ConnectMVKBulder.sln
dotnet test
dotnet run --project samples\Mvk.Sample.Console
```

Требуется .NET SDK 8.0+. Пример поддерживает режим опроса:

```powershell
dotnet run --project samples\Mvk.Sample.Console -- watch      # до Ctrl+C
dotnet run --project samples\Mvk.Sample.Console -- watch 10   # 10 секунд
```

Или минимальный пример опроса полосы 10…1000 Гц:

```powershell
dotnet run --project samples\Mvk.Sample.Polling                     # до Ctrl+C
dotnet run --project samples\Mvk.Sample.Polling -- 10.20.0.211 10 1  # хост, секунды, канал
```

### 4.17. Где взять адреса регистров?

- `Mvk01RegisterMap` — константы и формулы (`ChannelConfig`, `ChannelData`,
  `TachoConfig`, `TachoData`, `MeasurementByNumber`);
- [MVK01_registers.txt](../MVK01_registers.txt) — первоисточник;
- [Карта регистров и кодеки МВК01](07a-mvk01-register-map-and-codec.md) —
  разбор по разделам с примерами.

Осторожно с примером N=48 в `MVK01_registers.txt`: указанный адрес `0x41F0`
ошибочен, фактический — `0x42F0` (проверено на устройстве).

### 4.18. Как прочитать сырые регистры и декодировать float?

```csharp
ushort[] registers = await raw.ReadHoldingRegistersAsync(0x1004, 2, cts.Token);
float value = RegisterCodec.ToSingle(registers, raw.Settings.ByteOrder);
```

`raw` — это `IMvkDevice` (`mvk.Device.Device` у `Mvk01Connection` или
`pool.GetDevice("mvk-1")`). Всегда берите порядок байт из
`raw.Settings.ByteOrder`, а не «на глазок»: неверный порядок даёт правдоподобный,
но неверный float.

### 4.19. Почему `LastError` не null, хотя `State` равен `Connected`?

Потому что нетранзиентная ошибка (`DeviceError`, `Validation`) не меняет состояние
устройства: соединение-то есть, ошибка — в запросе или данных. `LastError`
обновляется без события `StateChanged` и сбрасывается только при следующем
успешном запросе. Для UI обновляйте снимок `State`/`LastError` по таймеру
или вызывайте `PrintDeviceState` из 1.2. И помните: `IsConnected == false` при
`State == Connected` — нормальная ситуация после ошибки транспорта.

### 4.20. Как отличить ошибку связи от ошибки данных?

По `MvkModbusException.IsTransient`: `true` — `Transport`, `Timeout`, `Busy`
(связь, повторяемо, реконнект автоматический); `false` — всё остальное
(`DeviceError`, `Validation`, `Configuration`, `DeviceNotFound`, `DeviceDisabled`,
`Unknown`). Полная таблица категорий — в разделе «Ошибки» README и в главе
[Ошибки](08-errors-logging-metrics.md).

```csharp
catch (MvkModbusException ex) when (ex.IsTransient)
{
    // связь: продолжаем цикл, реконнект уже идёт
}
catch (MvkModbusException ex)
{
    // данные/конфигурация: исправляем запрос или настройки
}
```

### 4.21. Что означают ошибки Modbus 0x01 и 0x02?

- `0x01` (illegal function/нечётный запрос) — типично для МВК01 при нечётном
  адресе или нечётном числе регистров; выравнивайте запросы и включайте
  `RequireEvenAddressAndQuantity`;
- `0x02` (illegal data address) — адрес вне карты регистров: сверяйтесь
  с `Mvk01RegisterMap`;
- код и функция всегда есть в сообщении:
  `Устройство 'mvk-1' вернуло ошибку Modbus 0x01 (функция 0x83).`,
  а машинный код — в `MvkModbusException.ModbusExceptionCode`.

### 4.22. Почему значения выглядят мусором?

Первым делом проверьте `ByteOrder` (регистр `0x000C`) и адрес: неверный порядок
байт и чтение по чужому адресу дают `1.4E-38`/`NaN`/огромные числа. Второе —
тип данных спектра (регистр `0x001A`): 0 — ускорение, 1 — скорость,
2 — перемещение; «не те» единицы часто объясняются именно этим. Третье —
чтобы исключить свою ошибку адресации, сравните сырое чтение с
`ReadChannelAsync(1)`: значение СКЗ из `RmsSets` должно совпадать с параметром
номер 49–72 и с прямым чтением смещений блока канала.

---

## 5. Если ничего не помогло

Соберите полную диагностику — обычно уже на шаге 2–3 становится ясно, где потеря.

### 5.1. Включите Debug-логи

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Mvk.Modbus": "Debug",
      "Mvk.MVK01": "Debug"
    }
  }
}
```

```csharp
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);
builder.Logging.AddFilter("Mvk.Modbus", LogLevel.Debug);
```

В Debug-лог попадут: состав пула, установка/закрытие соединений, реконнекты,
успешные запросы с адресами и длительностью, неудачные попытки ретраев.

### 5.2. Снимите состояние устройства и метрики

Используйте дамп из 1.2, а метрики можно посмотреть без внешних систем:

```csharp
using System.Diagnostics.Metrics;

using MeterListener listener = new();
listener.InstrumentPublished = (instrument, l) =>
{
    if (instrument.Meter.Name == "Mvk.Modbus")
    {
        l.EnableMeasurementEvents(instrument);
    }
};
listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
    Console.WriteLine($"{instrument.Name}: {value} [{string.Join(", ", tags.ToArray().Select(t => $"{t.Key}={t.Value}"))}]"));
listener.Start();
```

Счётчик `mvk.modbus.requests` с тегами `device`/`operation`/`result` покажет,
какие именно запросы падают и как часто. Если у запросов стабильно
`result=error`, а `MvkErrorKind` = `Timeout` — проблема на уровне сети/адреса;
если `Validation` — запрос формируется неправильно.

### 5.3. Проверьте связь «эталонным» сырым чтением

Регистры `0x1000` (серийный номер, 4 регистра) читаются всегда, если связь есть,
и не зависят от расчётных параметров DSP:

```csharp
using Mvk.Modbus.Protocol;
using Mvk.MVK01;

await using Mvk01Connection mvk = await Mvk01Connection.ConnectAsync("10.20.0.211");
IMvkDevice raw = mvk.Device.Device;

ushort[] serial = await raw.ReadHoldingRegistersAsync(0x1000, 4);
Console.WriteLine($"Сырые регистры: {string.Join(" ", serial.Select(r => r.ToString("X4")))}");
Console.WriteLine($"Серийный номер: '{Mvk01Codec.DecodeText(serial, 8)}'");

ushort[] version = await raw.ReadHoldingRegistersAsync(0x1004, 2);
Console.WriteLine($"Версия ПО   : {Mvk01Codec.DecodeFirmwareVersion(RegisterCodec.ToUInt32(version, raw.Settings.ByteOrder))}");

ushort[] errors = await raw.ReadHoldingRegistersAsync(0x1006, 2);
Console.WriteLine($"Ошибки      : 0x{RegisterCodec.ToUInt32(errors, raw.Settings.ByteOrder):X8}");
```

Расшифровка результатов:

- чтение падает с `Timeout`/`Transport` — связь, конфигурация или сеть;
- чтение успешно, но остальные параметры нет — адресация/чётность;
- чтение успешно, а значения не совпадают с ожиданиями — `ByteOrder`,
  номер канала/параметра;
- регистр ошибок не ноль — смотрите биты `DspReceiveError`/`SdCardError`/
  `DspSendError`/`ModbusInitializationError`/`ConfigurationCrcError`
  в `Mvk01DeviceInfo.Errors`.

### 5.4. Проверка без железа и на известном устройстве

- Чтобы отделить приложение от сети, поднимите эмулятор из `Mvk.Testing`
  (`MvkTestServer`) и повторите сценарий на нём — это исключает ошибки
  адресации и настроек из уравнения.
- Если в сети есть известное рабочее устройство, проверьте библиотеку на нём.
  В репозитории для этого используется **10.20.0.211:502** (SlaveId 1):
  подключайтесь и **только читайте**; запись регистров, Save и Reboot
  на реальном устройстве выполняйте лишь с явного разрешения.
- Для проверки недоступности/таймаутов используйте `SilentTcpServer` из
  `Mvk.Testing`: он принимает соединения и молчит.

### 5.5. Что приложить к обращению

- точный текст исключения (`ErrorKind`, `Message`, `ModbusExceptionCode`,
  `InnerException`);
- `State`, `IsConnected`, `LastError`, `Settings.Endpoint`;
- версию ПО устройства (`FirmwareVersion`) и серийный номер;
- фрагмент конфигурации `MvkModbus` **без секретов** (адреса, `SlaveId`,
  `ByteOrder`, `Pipeline`, `WritePolicy`);
- Debug-лог за момент сбоя и, если возможно, результат «эталонного» чтения
  `0x1000 ×4` из 5.3;
- версию/коммит библиотеки и как она подключена (NuGet/ProjectReference).

---

## См. также

- [Ошибки](08-errors-logging-metrics.md) — полный справочник `MvkErrorKind`,
  `MvkModbusException`, логирование и метрики.
- [Конфигурация](03-configuration.md) — секция `MvkModbus`, `Defaults`, `Devices`,
  валидация и загрузка JSON.
- [Пул](05-device-pool.md) — состояния устройств, реконнект, `AddDevice`/
  `RemoveDevice`, подводные камни.
- [README](../README.md) — быстрый старт, обработка ошибок, особенности МВК01,
  частые вопросы.
- [Пайплайн middleware](06-pipeline-middleware.md) — порядок middleware,
  таймауты и короткое замыкание.
- [Карта регистров и кодеки МВК01](07a-mvk01-register-map-and-codec.md) —
  адреса, формулы и декодирование значений.
- [Справочник API Mvk.Modbus](12a-api-reference-mvk-modbus.md) — точные
  сигнатуры публичных типов.
- [PLAN.md](../PLAN.md) и [MVK01_registers.txt](../MVK01_registers.txt) —
  ключевые решения и первоисточник карты регистров.
