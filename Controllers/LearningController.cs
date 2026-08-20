using Microsoft.AspNetCore.Mvc;
using OpenSearch.Client;
using OpenSearchLearningLab.Models;
using OpenSearchLearningLab.OpenSearch;

namespace OpenSearchLearningLab.Controllers;

// Endpoints in this controller exist purely to let you SEE what OpenSearch
// is doing internally. Nothing here is part of the "real" product API —
// they wrap OpenSearch's own diagnostic APIs (_analyze, _explain) so you
// can poke at tokenization and scoring directly, from the browser or curl,
// without needing to talk to OpenSearch's HTTP API by hand.
[ApiController]
[Route("learning")]
public class LearningController : ControllerBase
{
    private readonly IOpenSearchClient _client;

    public LearningController(IOpenSearchClient client)
    {
        _client = client;
    }

    // GET /learning/analyze?text=Apple%20iPhone%2015%20Pro%20Max
    //
    // Calls OpenSearch's _analyze API with the SAME "standard" analyzer
    // that the `name`/`description` fields use (see OpenSearchIndexSetup).
    // This shows you exactly what tokens get written into the inverted
    // index for a piece of text — the tokens returned here are the ones a
    // `match` query search term gets compared against. Nothing here is
    // hardcoded; the tokens come straight from OpenSearch.
    [HttpGet("analyze")]
    public async Task<IActionResult> Analyze([FromQuery] string text)
    {
        var response = await _client.Indices.AnalyzeAsync(a => a
            .Index(OpenSearchIndexSetup.IndexName)
            .Analyzer("standard")
            .Text(text));

        return Ok(new
        {
            text,
            tokens = response.Tokens?.Select(t => t.Token).ToList() ?? new List<string>()
        });
    }

    // GET /learning/explain/{productId}?q=iphone
    //
    // Runs the same kind of multi_match query the search endpoint uses,
    // scoped to ONE document, and asks OpenSearch to explain the score it
    // would give that document. The response is OpenSearch's own nested
    // breakdown (term frequency, inverse document frequency, field length
    // normalization, ...) — this project does not compute or reformat the
    // score itself. See README "BM25 / _score" for how to read it.
    [HttpGet("explain/{productId}")]
    public async Task<IActionResult> Explain(string productId, [FromQuery] string q)
    {
        var response = await _client.ExplainAsync<ProductDocument>(productId, e => e
            .Index(OpenSearchIndexSetup.IndexName)
            .Query(query => query
                .MultiMatch(m => m
                    .Query(q)
                    .Fields(f => f
                        .Field(x => x.Name, boost: 2.0)
                        .Field(x => x.Description)))));

        return Ok(new
        {
            productId,
            query = q,
            matched = response.Matched,
            explanation = response.Explanation
        });
    }
}
