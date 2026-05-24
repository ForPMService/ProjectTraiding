using History_DataMoex.Contracts.Serialization;
using History_DataMoex.Endpoints;
using History_DataMoex.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Добавляем сервисы в контейнер.

builder.Services.AddMoexClients(builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(options=>
{
    
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
});

builder.Services.AddOpenApi();

var app = builder.Build();
app.MapDebugEndpoints();

// Экспонируемые эндпоинты исходных данных: возвращают DTO MOEX напрямую.
// Это не финальное production API. В будущем /v1 эндпойнты будут возвращать модели Базы.
app.MapReferenceEndpoints();

// ALGOPACK-эндпоинты исходных данных: возвращают DTO MOEX напрямую.
// Жёстко заданные инструменты и диапазоны дат сохранены в рамках этой задачи.
app.MapAlgopackEndpoints();

// Календарные эндпоинты исходных данных: возвращают DTO MOEX напрямую.
// В будущем календарный /v1 API будет использовать нормализованные модели календаря и модеи Базы.
app.MapCalendarEndpoints();

app.MapRealtimeDebugEndpoints();

app.MapRealtimeDiagnosticEndpoints();

app.UseHttpsRedirection();

if(app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Run();
