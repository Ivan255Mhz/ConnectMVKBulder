# План разработки: ConnectMVKBulder

Библиотека-обёртка для работы с устройством МВК01 (Модбас/TCP) на C#.

Справочник регистров: [MVK01_registers.txt](MVK01_registers.txt) (ПО 1.0.61).

## Ключевые решения

- Транспорт: **NModbus 3.0.81** (`TcpClientAdapter` + `ModbusIpMaster`, async-методы, таймауты через `StreamResource`).
- TFM: **net8.0**, `Nullable enable`, `ImplicitUsings enable`.
- Конфигурация: **Microsoft.Extensions.Configuration/Options/DI** + JSON-секция `MvkModbus`.
- Пул: **гибридная модель** — ленивое подключение, сериализация запросов на устройство,
  автореконнект с backoff, idle-отключение, фоновый health-check.
- Обработка запросов: **middleware-пайплайн** в стиле ASP.NET
  (`InvokeModbusDelegate` + `IModbusMiddleware`, short-circuit, ретраи, кэш, метрики).
- Типизированный слой **Mvk.MVK01** по карте регистров (каналы АЦП, тахометры, спектры, конфигурация).
- Именование: решение `ConnectMVKBulder`, библиотеки `Mvk.Modbus`, `Mvk.MVK01`.

## Принципы простоты (обязательные)

- Простейший сценарий — три строки: `Mvk01Connection.ConnectAsync(host)` → `ReadChannelAsync(1)` → декодированные значения.
- Все параметры (порт 502, SlaveId 1, BigEndian, таймауты, ретраи, автореконнект) имеют рабочие значения по умолчанию.
- Один вызов `ReadChannelAsync` = полный снимок канала; запросы к устройству библиотека делает сама
  (чанки не более 125 регистров).
- Значения декодированы (float, строки, битовые поля, статусы) — никаких ручных `BitConverter` в прикладном коде.
- Ошибки — одно исключение `MvkModbusException` с понятным текстом; связь восстанавливается автоматически.
- JSON-конфиг и middleware опциональны: минимальный конфиг устройства — `{ "Id", "Host" }`.
- «Сложный» API (пул, пайплайн, сырые регистры) доступен, но не обязателен.
- Публичные API только асинхронные; синхронные обёртки не добавляем.

## Структура решения

```
ConnectMVKBulder.sln
src/
  Mvk.Modbus/          — транспорт, пул, пайплайн, конфигурация, DI
  Mvk.MVK01/           — карта регистров МВК01, кодеки, фасад устройства
tests/
  Mvk.Testing/         — in-process эмулятор Modbus-slave (NModbus)
  Mvk.Modbus.Tests/    — тесты пула, пайплайна, конфигурации, ретраев
  Mvk.MVK01.Tests/     — тесты адресации, кодеков, фасада
samples/
  Mvk.Sample.Console/  — пример использования
```

## Этапы

- [x] **Этап 0. Каркас.** Решение, `Directory.Build.props`, проекты, `.gitignore`, сборка зелёная.
- [x] **Этап 1. Ядро Mvk.Modbus.** Исключения (`MvkModbusException`, `MvkErrorKind`),
  `ModbusRequest`/`ModbusResponse`/`ModbusOperation`, опции (`MvkModbusOptions`, `MvkDeviceOptions`,
  `MvkDefaultsOptions`), валидация, загрузка JSON.
- [x] **Этап 2. Транспорт.** `IMvkTransport`, `NModbusTransport` (async, таймауты, ленивое
  подключение, авто-разрыв при ошибке), `RegisterCodec` (Big/Little/Mixed), эмулятор
  `Mvk.Testing` (Modbus-slave и молчащий сервер), тесты против эмулятора.
- [ ] **Этап 3. Пайплайн.** `ModbusRequestContext`, `InvokeModbusDelegate`, `IModbusMiddleware`,
  билдер, встроенные middleware: timeout, logging, validation, retry, metrics, writeProtection, caching.
- [ ] **Этап 4. Пул устройств.** `IMvkDevice`, `IMvkDevicePool`, `MvkDevice` (сериализация,
  состояния, реконнект), `MvkPoolReconnector` (idle-disconnect, health-check), события состояния.
- [ ] **Этап 5. DI и конфигурация.** `AddMvkModbus`, hosted-сервисы, пример `mvkmodbus.json`,
  тесты биндинга и валидации.
- [ ] **Этап 6. Mvk.MVK01.** `Mvk01Connection` (простой вход без конфига), `Mvk01RegisterMap`,
  enums, `Mvk01SpectrumBands`, `Mvk01MeasurementNumber`, `Mvk01Codec`,
  `IMvk01Device`/`IMvk01DeviceFactory` (`ReadDeviceInfoAsync`, `ReadChannelAsync` — снимок канала,
  `ReadTachoAsync`, команды Save/Reboot/Restore), тесты.
- [ ] **Этап 7. Пример.** Консольное приложение: подключение, кастомный middleware, чтение
  спектра, прогон против эмулятора.

## Схема конфигурации

```json
{
  "MvkModbus": {
    "Defaults": {
      "Port": 502,
      "SlaveId": 1,
      "ByteOrder": "BigEndian",
      "ConnectTimeoutMs": 3000,
      "ReadTimeoutMs": 1000,
      "WriteTimeoutMs": 3000,
      "MaxRetries": 2,
      "RetryDelayMs": 150,
      "ReconnectBaseDelayMs": 500,
      "ReconnectMaxDelayMs": 30000,
      "IdleDisconnectSeconds": 120,
      "MaxConcurrentRequests": 1,
      "WritePolicy": "ProtectConfigArea"
    },
    "Pipeline": ["timeout", "logging", "validation", "retry", "writeProtection"],
    "Pool": { "ReconnectEnabled": true, "HealthCheckIntervalSeconds": 15 },
    "Devices": [
      {
        "Id": "mvk-1",
        "Name": "Агрегат 1",
        "Host": "10.20.0.204",
        "Enabled": true,
        "ByteOrder": "BigEndian",
        "Tags": { "asset": "АЭЦНСР 850" }
      }
    ]
  }
}
```

`ByteOrder` соответствует регистру 0x000C устройства: `BigEndian` (по умолчанию),
`Mixed` (0x99999999, для ВДМ82), `LittleEndian` (0x66666666).

## Контракт пайплайна

```csharp
public delegate Task InvokeModbusDelegate(ModbusRequestContext ctx, CancellationToken ct);

public interface IModbusMiddleware
{
    Task InvokeAsync(ModbusRequestContext ctx, InvokeModbusDelegate next);
}
```

Порядок middleware задаётся списком `Pipeline` в конфигурации (первый — самый внешний).
Middleware может: залогировать, изменить/проверить запрос, выполнить ретрай,
коротко замкнуть обработку (кэш), подменить ответ. Терминальный элемент пайплайна — транспорт.

## Риски

- NModbus async-методы не принимают `CancellationToken` — отмена через таймауты сокета
  и закрытие соединения при жёстком abort.
- `TcpClient.Connected` устаревает после разрыва — используем собственное состояние устройства
  и фоновый health-check.
- Большой пул: сокеты держатся только у активных устройств (idle-отключение),
  реконнект выполняется в фоне и не блокирует вызывающий код.
