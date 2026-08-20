using OpenSearch.Client;
using OpenSearchLearningLab.Models;

namespace OpenSearchLearningLab.OpenSearch;

public static class OpenSearchIndexSetup
{
    // The index name is versioned ("-v1") on purpose. When a field's TYPE
    // needs to change (e.g. category from keyword to a nested object),
    // OpenSearch cannot alter an existing mapping in place — you create
    // products-v2 with the new mapping and reindex into it. See the README
    // section "Reindexing" for why. Adding more DATA to existing fields is
    // fine and does not require a new index; changing a field's TYPE does.
    public const string IndexName = "products-v1";

    // Called once at application startup instead of letting OpenSearch
    // auto-create the index from the first document it sees. Dynamic
    // mapping (the default) *works*, but it means OpenSearch is guessing
    // field types from whatever JSON happens to arrive first — a price of
    // "100" becomes a long, not a double; a description becomes `text`
    // with default analysis you didn't choose. Creating the index
    // explicitly means every field type below is a deliberate decision.
    public static async Task EnsureIndexAsync(IOpenSearchClient client)
    {
        var exists = await client.Indices.ExistsAsync(IndexName);
        if (exists.Exists)
        {
            return;
        }

        await client.Indices.CreateAsync(IndexName, c => c
            // This lab runs a single-node OpenSearch cluster (see
            // docker-compose.yml), so a replica shard would have nowhere to
            // be assigned — there's no second node to host it. 0 replicas
            // keeps the cluster status "green" instead of a permanent
            // "yellow" from an unassignable replica. A real deployment
            // would set this to 1+ specifically so replicas (§26 in the
            // README) have somewhere to live.
            .Settings(s => s.NumberOfReplicas(0))
            .Map<ProductDocument>(m => m
                .Properties(p => p
                    // The id is an exact identifier, never full-text searched,
                    // so it is keyword — same reasoning as brand/category below.
                    // Left out of this list entirely, OpenSearch would still
                    // index it via dynamic mapping (inferring a type from the
                    // first document it sees) — exactly what §7/§9 in the
                    // README warns against relying on.
                    .Keyword(k => k.Name(n => n.Id))

                    // text: analyzed for full-text search. OpenSearch runs
                    // this through an analyzer (lowercase, tokenize on
                    // whitespace/punctuation, ...) and indexes the resulting
                    // TOKENS, not the raw string. This is what lets a search
                    // for "iphone" match "iPhone 15 Pro Max". See the README
                    // section "Text Analysis" for the full pipeline.
                    .Text(t => t.Name(n => n.Name))
                    .Text(t => t.Name(n => n.Description))

                    // keyword: NOT analyzed. Stored and indexed as one exact
                    // token, used for filtering, aggregations, and sorting —
                    // "Apple" only matches "Apple", never "apple" or "app".
                    // Category/brand are things we filter/facet on, not
                    // free-text search, so they are keyword, not text.
                    .Keyword(k => k.Name(n => n.Category))
                    .Keyword(k => k.Name(n => n.Brand))

                    // Numeric types. OpenSearch needs to know these are
                    // numbers so range queries (price >= X) and sorting work
                    // correctly — as text, "100000" would sort/compare
                    // lexicographically ("9" > "100000").
                    .Number(n => n.Name(f => f.Price).Type(NumberType.Double))
                    .Number(n => n.Name(f => f.Rating).Type(NumberType.Double))
                    .Number(n => n.Name(f => f.Popularity).Type(NumberType.Integer))
                )));
    }
}
