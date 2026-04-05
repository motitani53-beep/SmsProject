# CreateCampaign: Behavior When DB or RabbitMQ Is Unavailable

## Current flow (order of operations)

1. Create campaign entity → **SaveChangesAsync()** (DB)
2. Create delivery details → **SaveChangesAsync()** (DB)
3. **PublishCampaignToRabbitMq()** → for each delivery: **PublishJson()** (RabbitMQ)

All of this runs inside a single `try/catch`. Any exception returns **500** and the message "An error occurred while creating the campaign".

---

## When the database is NOT connected

| Step | What happens |
|------|----------------------|
| 1st or 2nd `SaveChangesAsync()` | Throws (e.g. `Npgsql.NpgsqlException` or similar). |
| Catch | Exception is caught, logged, API returns **500** with `details: ex.Message`. |
| Result | Client gets 500. **No campaign or delivery details are saved** (or only campaign if the first save succeeded and the second one failed — then you have partial data). |

So: **no DB = request fails, client sees 500.**

---

## When RabbitMQ is NOT connected

| Step | What happens |
|------|----------------------|
| 1–2 | Both `SaveChangesAsync()` succeed → **Campaign and all delivery details are already in the DB.** |
| 3 | `PublishCampaignToRabbitMq` → `_rabbitMqService.PublishJson` → `EnsureConnection()` or `BasicPublish` throws (e.g. `RabbitMQ.Client.Exceptions.BrokerUnreachableException`). |
| Catch | Exception is caught, logged, API returns **500**. |
| Result | Client gets 500 and thinks the campaign failed. **In reality:** campaign and recipients are saved in the DB, but **no messages were published to RabbitMQ**, so the Transmitter never sees them and no SMS is sent. |

So: **no Rabbit = DB is updated, but client gets 500 and no messages go to the queue.**

---

## Summary

| Scenario | DB state | Rabbit state | API response |
|----------|----------|--------------|-------------|
| DB down  | Nothing (or partial) saved | Not reached | **500** |
| Rabbit down | Campaign + delivery details **saved** | Nothing published | **500** |

---

## Possible improvements

1. **RabbitMQ failure resilience**  
   Wrap only the publish step in try/catch. On success: return 200 as today. On Rabbit failure: still return **200** (campaign created), but add a response field like `"queuePublished": false` and log a warning so you can republish or retry later (e.g. background job that publishes pending campaigns).

2. **DB transaction**  
   Use a single transaction so that if the second `SaveChangesAsync()` fails, the campaign insert is rolled back (no partial campaign without delivery details).

3. **Health checks**  
   Add ASP.NET Core health checks for DB and RabbitMQ so you can see connectivity status (e.g. `/health`) before calling CreateCampaign.

If you want, we can implement (1) and/or (2) in the code next.
