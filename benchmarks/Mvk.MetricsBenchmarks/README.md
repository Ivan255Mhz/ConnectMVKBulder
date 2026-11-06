# Mvk.MetricsBenchmarks

In-process бенчмарк пайплайна `Mvk.Modbus`. Сравнивает накладные расходы
middleware без сети и реальных устройств:

- без middleware;
- `ValidationMiddleware`;
- `MetricsMiddleware` без слушателя;
- `MetricsMiddleware` с `MeterListener`;
- `LoggingMiddleware` с `NullLogger` и с включённым no-op логгером;
- полный набор middleware по умолчанию.

Каждый кейс выполняется несколько раундов, в отчёт попадает лучший раунд.
`alloc/op` считается через `GC.GetTotalAllocatedBytes`.

## Запуск

```powershell
dotnet run --project benchmarks\Mvk.MetricsBenchmarks -- --iterations 1000000 --warmup 100000 --rounds 5
```

Параметры:

| Параметр | По умолчанию | Смысл |
|---|---|---|
| `--iterations` | `200000` | Операций в одном раунде |
| `--warmup` | `20000` | Прогревочных операций |
| `--rounds` | `5` | Раундов на кейс, в отчёт идёт лучший |
| `--help` | — | Справка |

Бенчмарк не использует сеть и не обращается к МВК01.
