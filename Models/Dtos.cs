namespace OpenSearchLearningLab.Models;

// Plain request/response shapes for the API. No AutoMapper, no separate
// "contracts" project — just the smallest classes that make the endpoints
// readable.

public class CreateProductRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public double Rating { get; set; }
    public int Popularity { get; set; }
}

public class UpdateProductRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public double Rating { get; set; }
    public int Popularity { get; set; }
}

public class SearchResultItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public double Rating { get; set; }

    // The BM25 relevance score OpenSearch assigned this document for the
    // query that was run. Higher = considered more relevant. See the
    // README section "Relevance / BM25 / _score" for what drives this.
    public double? Score { get; set; }
}

public class SearchResponse
{
    public long Total { get; set; }
    public List<SearchResultItem> Items { get; set; } = new();
}
