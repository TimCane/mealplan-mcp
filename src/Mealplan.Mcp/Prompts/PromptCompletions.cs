using Mealplan.Infrastructure.Reading;
using ModelContextProtocol.Protocol;

namespace Mealplan.Mcp.Prompts;

/// <summary>
/// Backs completion/complete for the prompt arguments: allergens, cuisines,
/// sources and dislikes autocomplete from the live vocabularies, so a client
/// offers real slugs instead of leaving the user to guess ones that silently
/// match nothing. Candidates keep the vocabulary views' recipe-count order -
/// the busiest value is the likeliest pick.
/// </summary>
public class PromptCompletions(RecipeQueryService service)
{
    /// <summary>Completion values are capped at this many per the MCP spec.</summary>
    private const int MaxValues = 100;

    public async Task<CompleteResult> CompleteAsync(
        CompleteRequestParams? request,
        CancellationToken ct)
    {
        if (request?.Ref is not PromptReference || request.Argument is null)
        {
            return Empty();
        }

        // List-shaped arguments are comma separated, so only the segment being
        // typed completes; the settled ones ride along in front.
        var value = request.Argument.Value ?? string.Empty;
        var cut = value.LastIndexOf(',');
        var head = cut < 0 ? string.Empty : value[..(cut + 1)] + " ";
        var tail = (cut < 0 ? value : value[(cut + 1)..]).Trim();

        var candidates = request.Argument.Name switch
        {
            "allergens" => Prefixed(
                (await service.ListAllergensAsync(take: MaxValues, ct: ct))
                    .Items.Select(a => a.Slug),
                tail),
            "cuisine" => Prefixed(
                (await service.ListCuisinesAsync(take: MaxValues, ct: ct))
                    .Items.Select(c => c.Slug),
                tail),
            "sources" => Prefixed(
                (await service.ListSourcesAsync(ct)).Select(s => s.Source),
                tail),

            // Ingredients are a catalogue, not a vocabulary: the service
            // substring-filters server-side, so "clove" still finds
            // "Garlic Clove" and no prefix check applies on top.
            "dislikes" => (await service.SearchIngredientsAsync(tail, take: MaxValues, ct: ct))
                .Items.Select(i => i.Name),

            _ => [],
        };

        var matches = candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new CompleteResult
        {
            Completion = new Completion
            {
                Values = matches.Take(MaxValues).Select(m => head + m).ToList(),
                Total = matches.Count,
                HasMore = matches.Count > MaxValues,
            },
        };
    }

    private static IEnumerable<string> Prefixed(IEnumerable<string> values, string tail) =>
        values.Where(v => v.StartsWith(tail, StringComparison.OrdinalIgnoreCase));

    private static CompleteResult Empty() => new()
    {
        Completion = new Completion { Values = [] },
    };
}
