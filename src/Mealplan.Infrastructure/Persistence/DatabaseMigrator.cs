using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mealplan.Infrastructure.Persistence;

/// <summary>
/// Migrates every DbContext registered in the container, whichever source
/// contributed it. Sources own their schema and their migration history, so the
/// host must not need to know their names to bring them up to date.
/// </summary>
public class DatabaseMigrator(IReadOnlyList<Type> contextTypes)
{
    public async Task MigrateAllAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();

        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger<DatabaseMigrator>();

        foreach (var type in contextTypes)
        {
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(type);

            logger.LogInformation("Migrating {Context}", type.Name);
            await context.Database.MigrateAsync(ct);
        }
    }
}
