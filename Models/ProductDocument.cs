namespace OpenSearchLearningLab.Models;

// This is deliberately a SEPARATE class from Product, even though the fields
// look identical right now. That separation is the whole point:
//
//   PostgreSQL Product        →  what the business considers true
//   OpenSearch ProductDocument →  what the search engine is allowed to search on
//
// In a real system these diverge quickly: the document might add a
// denormalized "brandAndCategory" field for faceting, drop internal-only
// columns, flatten a related table into an array, or reshape a price into
// a currency-aware object. Treating "index the document" as "serialize the
// row" is the mistake this class exists to prevent.
//
// The Id is a string here because OpenSearch document IDs are always
// strings internally, even when the source key is a PostgreSQL integer.
public class ProductDocument
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public double Rating { get; set; }
    public int Popularity { get; set; }
}
