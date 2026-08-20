# OpenSearch Learning Lab

A small, deliberately unfinished-looking .NET 8 project whose only purpose is
to teach **OpenSearch from first principles**, using a tiny product catalog
as the vehicle. It is not a production template. There is no auth, no
message queue, no microservices — see [§34](#34-things-this-project-deliberately-does-not-do) for the
full "not here" list.

The one thing this project optimizes for: when you open a file, you should
immediately understand what it does and why it exists in the OpenSearch
lifecycle.

```text
Create Product
    │
    ▼
PostgreSQL
    │
    ▼
Convert Product → OpenSearch ProductDocument
    │
    ▼
Index Document
    │
    ▼
OpenSearch Index
    │
    ▼
Inverted Index
    │
    ▼
Analysis / Tokenization
    │
    ▼
Search Query
    │
    ▼
OpenSearch Query Execution
    │
    ▼
Scoring (BM25)
    │
    ▼
Search Results
```

## Table of contents

1. [What are we building?](#1-what-are-we-building)
2. [Why PostgreSQL + OpenSearch?](#2-why-postgresql--opensearch)
3. [Running the project](#3-running-the-project)
4. [Product creation flow](#4-product-creation-flow)
5. [The PostgreSQL product](#5-the-postgresql-product)
6. [The OpenSearch product document](#6-the-opensearch-product-document)
7. [Mapping](#7-mapping)
8. [Indexing](#8-indexing)
9. [Text analysis](#9-text-analysis)
10. [Tokenization](#10-tokenization)
11. [The inverted index](#11-the-inverted-index)
12. [Term dictionary](#12-term-dictionary)
13. [Postings](#13-postings)
14. [Search](#14-search)
15. [Query DSL](#15-query-dsl)
16. [Match query](#16-match-query)
17. [Term query](#17-term-query)
18. [Range query](#18-range-query)
19. [Bool query](#19-bool-query)
20. [Filtering](#20-filtering)
21. [Relevance](#21-relevance)
22. [BM25](#22-bm25)
23. [`_score`](#23-_score)
24. [Explain API](#24-explain-api)
25. [Shards](#25-shards)
26. [Replicas](#26-replicas)
27. [Lucene](#27-lucene)
28. [Segments](#28-segments)
29. [Refresh](#29-refresh)
30. [Segment merging](#30-segment-merging)
31. [Updates](#31-updates)
32. [Deletes](#32-deletes)
33. [`_source`](#33-_source)
34. [Reindexing](#34-reindexing)
35. [PostgreSQL vs OpenSearch](#35-postgresql-vs-opensearch)
36. [Data synchronization](#36-data-synchronization)
37. [Learning experiments](#37-learning-experiments)
38. [Things this project deliberately does not do](#38-things-this-project-deliberately-does-not-do)

Throughout, the teaching order is always:

```text
Concept → tiny example → what OpenSearch does internally → actual JSON → C# code → how to observe it yourself
```

---

## 1. What are we building?

A Web API with three moving parts:

- **PostgreSQL** — the source of truth for products (a normal relational table).
- **OpenSearch** — a separate search index built *from* those products, used only for search.
- **.NET 8 Web API** — the glue: converts rows into search documents, indexes them, and runs search queries.

```text
                    PostgreSQL
                        │
                        │ Product (row)
                        ▼
                 .NET Web API
                        │
                        │ ProductDocument (converted)
                        ▼
                  OpenSearch
                        │
                        ▼
                  Index / Shard
                        │
                        ▼
                     Lucene
                        │
                        ▼
                 Inverted Index
```

## 2. Why PostgreSQL + OpenSearch?

PostgreSQL is excellent at being a transactional source of truth: strong
consistency, foreign keys, ACID transactions. It is mediocre at full-text
relevance search — `ILIKE '%iphone%'` does not rank results, does not
understand that "iPhone" and "iphone" are the same term, and gets slow on
large text columns without dedicated extensions.

OpenSearch is excellent at search: analyzed text, relevance ranking,
faceting, fast filtering over millions of documents. It is not meant to be
your only datastore — it does not do multi-document ACID transactions the
way PostgreSQL does, and rebuilding its data from scratch should always be
possible from a real source of truth.

So: PostgreSQL owns the data, OpenSearch owns making that data searchable.
This project keeps both, on purpose, so you can watch data flow from one to
the other and see where the two systems' jobs actually differ.

## 3. Running the project

Start infrastructure:

```bash
docker compose up -d
```

This starts PostgreSQL (`localhost:5433`), OpenSearch (`localhost:9200`),
and OpenSearch Dashboards (`localhost:5601`).

Run the API:

```bash
dotnet run
```

On startup the app will (see `Program.cs`):

1. Create the `products` table in PostgreSQL if it doesn't exist.
2. Create the `products-v1` OpenSearch index with an **explicit mapping** if it doesn't exist.
3. Seed ~15 example products into both stores if the table is empty.

The console log will print every OpenSearch request as raw JSON — this is
intentional (see [§8](#8-indexing) and [§20 in the original brief](#15-query-dsl)) so you can see exactly what
DSL the app sends.

### Endpoints

| Method | Path | Purpose |
|---|---|---|
| POST | `/products` | Create a product (Postgres + OpenSearch) |
| PUT | `/products/{id}` | Full update (Postgres + OpenSearch) |
| DELETE | `/products/{id}` | Delete (Postgres + OpenSearch) |
| GET | `/products/search?q=&brand=&minPrice=&maxPrice=&minRating=` | Search |
| GET | `/learning/analyze?text=` | Calls OpenSearch `_analyze` |
| GET | `/learning/explain/{productId}?q=` | Calls OpenSearch `_explain` |

### Example curl commands

```bash
curl -X POST http://localhost:5000/products \
  -H "Content-Type: application/json" \
  -d '{"name":"iPhone 15 Pro Max","description":"Apple flagship smartphone with titanium design","category":"Smartphones","brand":"Apple","price":120000,"rating":4.8,"popularity":95}'

curl "http://localhost:5000/products/search?q=iphone"

curl -X PUT http://localhost:5000/products/1 \
  -H "Content-Type: application/json" \
  -d '{"name":"iPhone 15 Pro Max","description":"Updated description","category":"Smartphones","brand":"Apple","price":115000,"rating":4.9,"popularity":97}'

curl -X DELETE http://localhost:5000/products/1

curl "http://localhost:5000/learning/analyze?text=Apple%20iPhone%2015%20Pro%20Max"

curl "http://localhost:5000/learning/explain/2?q=iphone"
```

(Port is whatever `dotnet run` prints — typically `5000`/`5100`-something for HTTP; check the console.)

## 4. Product creation flow

```text
HTTP POST /products
    │
    ▼
ProductsController.Create
    │
    ▼
ProductService.CreateAsync
    │
    ├── 1. new Product { ... }
    ├── 2. _db.Products.Add(product); SaveChangesAsync()   ← PostgreSQL row now exists
    ├── 3. ProductDocumentMapper.ToDocument(product)         ← convert
    └── 4. _openSearch.IndexAsync(document, ...)             ← OpenSearch document now exists
```

Open [`Services/ProductService.cs`](Services/ProductService.cs) — `CreateAsync` reads top to
bottom exactly like this diagram. Nothing is hidden behind a repository
interface or a mediator; it's four sequential steps in one method.

## 5. The PostgreSQL product

[`Models/Product.cs`](Models/Product.cs):

```csharp
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string Category { get; set; }
    public string Brand { get; set; }
    public decimal Price { get; set; }
    public double Rating { get; set; }
    public int Popularity { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

This is a boring relational row. It knows nothing about analyzers, tokens,
or search relevance — that is entirely OpenSearch's concern, applied to a
*different* class described next.

## 6. The OpenSearch product document

[`Models/ProductDocument.cs`](Models/ProductDocument.cs) looks almost identical to `Product`
today, and that similarity is misleading — **it is a different class on
purpose**:

```text
PostgreSQL Product
        │
        │ application explicitly converts
        ▼
OpenSearch ProductDocument
```

The document is *not* "the database row serialized to JSON." It is
whatever shape makes the product searchable well. In this tiny project the
fields happen to match one-for-one, but in any real system they diverge —
the document might:

- drop internal-only columns (e.g. an internal SKU cost) that should never be searchable or returned,
- add fields that don't exist in the row at all (e.g. a denormalized `brandCategory` facet field, or a full-text `searchText` that concatenates several columns),
- reshape a one-to-many relation (e.g. `Product.Reviews`) into a nested array,
- store a different data type than the column (e.g. a `Price` in cents as a `long` instead of `decimal`).

`ProductDocumentMapper.ToDocument(product)` in
[`OpenSearch/ProductDocumentMapper.cs`](OpenSearch/ProductDocumentMapper.cs) is the one place
this conversion happens.

## 7. Mapping

**Concept.** A mapping is OpenSearch's schema for an index: for every
field, it declares the field's *type* (`text`, `keyword`, numeric, date,
boolean, object, nested, ...), which determines how that field is analyzed,
stored, and queried.

**Why OpenSearch needs one.** JSON alone doesn't tell OpenSearch enough.
The string `"120000"` could be a `keyword` (only matches "120000" exactly),
a `text` field (tokenized, matches "120000" as a word), or a numeric type
(supports `>=`/`<=` range queries and correct sorting). The mapping is what
resolves that ambiguity, once, up front.

**This project's mapping** (`OpenSearch/OpenSearchIndexSetup.cs`), created
explicitly at startup instead of left to dynamic mapping:

| Field | Type | Why |
|---|---|---|
| `name` | `text` | Free-text search — "iphone" should match "iPhone 15 Pro Max". Needs analysis. |
| `description` | `text` | Same reasoning — long-form text you search *into*, not compare exactly. |
| `category` | `keyword` | Exact-match filtering/faceting ("Smartphones"); never partially searched. |
| `brand` | `keyword` | Same — you filter `brand = Apple`, you don't full-text search it. |
| `price` | `double` | Needs numeric range queries (`>=`, `<=`) and correct sort order. |
| `rating` | `double` | Same — range queries like `rating >= 4.5`. |
| `popularity` | `integer` | A whole number used for sorting/filtering, no decimals needed. |

Actual mapping JSON (what `client.Indices.CreateAsync` sends):

```json
PUT /products-v1
{
  "mappings": {
    "properties": {
      "name":        { "type": "text" },
      "description": { "type": "text" },
      "category":    { "type": "keyword" },
      "brand":       { "type": "keyword" },
      "price":       { "type": "double" },
      "rating":      { "type": "double" },
      "popularity":  { "type": "integer" }
    }
  }
}
```

**`text` vs `keyword`, concretely.** Given `brand: "Apple"`:

- as `keyword`: stored as the single exact token `Apple` (case preserved). A `term` query for `apple` (lowercase) will **not** match unless you lowercase your input, because `term` queries skip analysis entirely.
- as `text`: analyzed into `[apple]` (lowercased, by the default `standard` analyzer). A `match` query for `Apple`, `apple`, or `APPLE` all match, because the query text goes through the same analyzer before comparison.

**Numeric fields as text, a cautionary example.** If `price` were mapped
(or dynamically inferred) as `keyword` instead of `double`, a range query
`price >= 100000` would be rejected or behave incorrectly — `keyword`
fields don't support numeric range semantics, and sorting would be
lexicographic (`"9000"` would sort after `"100000"`, alphabetically).

**Other field types this project doesn't use, but you should know:**

- **date** — stores an ISO-8601 (or configured format) timestamp; supports range queries and date-math (`now-7d/d`).
- **boolean** — `true`/`false`; a keyword-like exact match under the hood.
- **object** — a nested JSON object flattened into `dotted.field.names` internally. A search for a field inside it works, but the relationship between sibling values across array entries can be lost (see nested below).
- **nested** — like `object`, but each entry in an array is indexed as a hidden separate document, so a query can require *the same array entry* to match multiple conditions (e.g. "a review with rating 5 AND text containing 'great'" — without `nested`, a match on rating from one review and text from a different review could incorrectly combine).

**What happens when a document doesn't match the mapping?** If a field is
mapped `double` and a document sends `"price": "not-a-number"`, that
document is **rejected** with a mapping/parsing exception at index time —
it never gets indexed at all. Mapping conflicts fail loudly, per document,
not silently.

**Can mappings change after documents exist?** You can usually **add** new
fields to a mapping at any time — new fields just start getting indexed
going forward for new/updated documents. You generally **cannot change an
existing field's type** (e.g. `keyword` → `text`) once any document has
been indexed with the old mapping, because the underlying Lucene data
structures for that field were already built for the old type. This is
exactly why [reindexing](#34-reindexing) into a new index exists as a
concept — see that section.

## 8. Indexing

**Concept.** "Indexing a document" means handing OpenSearch a JSON document
plus an id, and OpenSearch does everything required to make that document
findable later.

What actually happens when `Services/ProductService.cs` runs:

```csharp
var indexResponse = await _openSearch.IndexAsync(document, i => i
    .Index(OpenSearchIndexSetup.IndexName)
    .Id(document.Id));
```

conceptually:

```text
HTTP request (POST/PUT /products-v1/_doc/{id})
    │
    ▼
OpenSearch receives the document
    │
    ▼
Document is parsed as JSON
    │
    ▼
Mapping determines each field's type
    │
    ▼
Text fields (name, description) are analyzed → tokens produced
    │
    ▼
Inverted index structures are updated with those tokens
    │
    ▼
Document becomes searchable (after the next refresh — see §29)
```

**Important distinction:** this application never touches an inverted
index, a term dictionary, or a Lucene segment directly. It sends JSON over
HTTP. OpenSearch (and, underneath it, Lucene) owns all of the internal
indexing machinery. This project's C# code stops at "send the document" —
everything below that line is OpenSearch's job, and the sections below
describe it at a conceptual level.

Observe it yourself:

```bash
curl http://localhost:9200/products-v1/_doc/1
curl http://localhost:9200/products-v1/_count
```

## 9. Text analysis

**Concept.** Before a `text` field's value is stored in the inverted index,
it passes through an **analyzer**:

```text
Analyzer
   │
   ▼
Character filters   (e.g. strip HTML)
   │
   ▼
Tokenizer            (split into candidate terms)
   │
   ▼
Token filters        (lowercase, remove stopwords, stem, ...)
   │
   ▼
Final indexed terms
```

This project uses OpenSearch's default `standard` analyzer on `name` and
`description` (no custom analyzer configured — see the mapping in
[§7](#7-mapping)). The `standard` analyzer: tokenizes on word boundaries (whitespace,
punctuation), then lowercases every token. It does not stem or remove
stopwords by default.

## 10. Tokenization

Take the string `"Apple iPhone 15 Pro Max"` through the `standard`
analyzer:

```text
"Apple iPhone 15 Pro Max"
        │  tokenizer splits on word boundaries
        ▼
["Apple", "iPhone", "15", "Pro", "Max"]
        │  lowercase token filter
        ▼
["apple", "iphone", "15", "pro", "max"]
```

These five lowercase tokens are what actually get written into the
inverted index for this field — not the original string.

This is why searching for `iphone` (lowercase, no punctuation) matches a
product named `"Apple iPhone 15 Pro Max"`: the query term `iphone` is
analyzed the same way at search time, becomes the token `iphone`, and that
token exists in the document's token list.

**Observe it yourself** — this project has a dedicated endpoint for
exactly this ([§14 below](#14-search) covers search; this is the analyze endpoint from
the brief):

```bash
curl "http://localhost:5000/learning/analyze?text=Apple%20iPhone%2015%20Pro%20Max"
```

```json
{
  "text": "Apple iPhone 15 Pro Max",
  "tokens": ["apple", "iphone", "15", "pro", "max"]
}
```

This calls OpenSearch's real `_analyze` API — the tokens are never
hardcoded in this project.

**Why `text` behaves differently from `keyword`:** a `keyword` field skips
this entire pipeline. `"Apple iPhone 15 Pro Max"` as a keyword is stored as
one single token, verbatim: `Apple iPhone 15 Pro Max`. A search for
`iphone` would never match it as a `term` query, because that exact string
was never produced as a token.

## 11. The inverted index

This is the single most important idea in this whole project.

**Traditional thinking:**

```text
Document → words
```

A document is a bag of words. To find documents containing "iphone", you'd
have to scan every document.

**Inverted index thinking:**

```text
Word → documents
```

Flip it: build a lookup from every word to the list of documents containing
it. Now finding documents for "iphone" is a direct lookup, not a scan.

**Tiny conceptual example.** Suppose we index three documents:

```text
Document 1: "Apple iPhone smartphone"
Document 2: "Samsung smartphone"
Document 3: "Apple laptop"
```

The (simplified) inverted index looks like:

```text
term         → documents
─────────────────────────
apple        → 1, 3
iphone       → 1
smartphone   → 1, 2
samsung      → 2
laptop       → 3
```

Searching for `smartphone` becomes: look up the key `smartphone`, get back
`[1, 2]`, done — no scanning of document 3 was ever needed.

> **Simplified mental model.** The real Lucene implementation is
> considerably more sophisticated — the table above is stored as a sorted
> [term dictionary](#12-term-dictionary) plus compressed [postings lists](#13-postings), with
> additional per-term statistics (document frequency, positions, offsets)
> used for scoring and phrase queries. Treat the "word → documents" table
> as the *idea*, not the literal file format.

## 12. Term dictionary

Inside a Lucene segment, all the unique terms for a field are stored
sorted, in a structure optimized for fast lookup and prefix search — the
**term dictionary**. Conceptually it's the sorted left-hand column from
the table in [§11](#11-the-inverted-index): `apple`, `iphone`, `laptop`, `samsung`,
`smartphone`. Looking up whether a term exists, and where its postings
list starts, is fast (effectively logarithmic / hash-assisted, not a linear
scan) because the dictionary is sorted and indexed itself.

## 13. Postings

For each term in the dictionary, Lucene stores a **postings list**: which
documents contain it, and (depending on what's needed) how many times, at
which positions, and in which fields. The right-hand column in the [§11](#11-the-inverted-index)
table (`iphone → 1`) is a postings list in miniature. Postings lists are
what a `match` query actually walks through at search time, and what term
frequency ([§22](#22-bm25)) is read from.

## 14. Search

The search endpoint:

```http
GET /products/search?q=iphone&brand=Apple&minPrice=100000&maxPrice=130000&minRating=4.5
```

```text
Search Query
     │
     ▼
OpenSearch receives query
     │
     ▼
Query is parsed / analyzed (query text goes through the same analyzer as the field)
     │
     ▼
Query fans out to each shard (§25)
     │
     ▼
Each shard's Lucene index is searched via the inverted index
     │
     ▼
Matching documents + scores collected per shard
     │
     ▼
Shard results merged, globally sorted by score
     │
     ▼
Top results returned as the API response
```

See [`Controllers/ProductsController.cs`](Controllers/ProductsController.cs) and
[`Services/ProductService.cs`](Services/ProductService.cs) for the actual query construction.

## 15. Query DSL

OpenSearch's Query DSL is JSON that describes a query. For every query type
below: raw JSON → C# code → what it does → when to use it.

### 16. Match query

**When:** searching analyzed `text` fields with natural-language input.

```json
{
  "query": {
    "match": { "name": "iphone" }
  }
}
```

```csharp
mu.Match(m => m.Field(f => f.Name).Query("iphone"))
```

`match` analyzes the query text (`"iphone"` → token `iphone`) the same way
the field was analyzed at index time, then looks that token up in the
inverted index for `name`. This is why it can match `"iPhone 15 Pro Max"` —
both sides go through the same analyzer, so both end up comparing the
token `iphone` to the token `iphone`.

This project actually uses `multi_match` in `ProductService.SearchAsync` to
search `name` and `description` together with `name` boosted:

```json
{
  "query": {
    "multi_match": {
      "query": "iphone",
      "fields": ["name^2", "description"]
    }
  }
}
```

### 17. Term query

**When:** exact-value matching against a `keyword` field — no analysis.

```json
{
  "query": {
    "term": { "brand": "Apple" }
  }
}
```

```csharp
f.Term(t => t.Field(x => x.Brand).Value("Apple"))
```

`term` does **not** analyze the input. Because `brand` is mapped
`keyword` (stored as the exact string `Apple`), `term` with value `Apple`
matches, but `term` with value `apple` does **not** — there is no
lowercasing step for `term` queries. This is the core distinction:

```text
match → analyzed search (query text is tokenized/lowercased first)
term  → exact term search (query value is compared as-is)
```

### 18. Range query

**When:** numeric or date comparisons (`>=`, `<=`, `>`, `<`).

```json
{
  "query": {
    "range": { "price": { "gte": 100000, "lte": 130000 } }
  }
}
```

```csharp
f.Range(r => r.Field(x => x.Price).GreaterThanOrEquals(100000).LessThanOrEquals(130000))
```

Only possible because `price` is mapped as a numeric type ([§7](#7-mapping)). Range
queries read the field's values directly (via a numeric structure Lucene
builds for numeric fields), not the inverted index of tokens.

### 19. Bool query

**When:** combining multiple conditions.

```json
{
  "query": {
    "bool": {
      "must":   [ { "multi_match": { "query": "iphone", "fields": ["name^2", "description"] } } ],
      "filter": [
        { "term":  { "brand": "Apple" } },
        { "range": { "price":  { "gte": 100000, "lte": 130000 } } },
        { "range": { "rating": { "gte": 4.5 } } }
      ]
    }
  }
}
```

```csharp
.Bool(b => b
    .Must(mu => mu.MultiMatch(m => m.Query("iphone").Fields(f => f.Field(x => x.Name, boost: 2.0).Field(x => x.Description))))
    .Filter(
        f => f.Term(t => t.Field(x => x.Brand).Value("Apple")),
        f => f.Range(r => r.Field(x => x.Price).GreaterThanOrEquals(100000).LessThanOrEquals(130000)),
        f => f.Range(r => r.Field(x => x.Rating).GreaterThanOrEquals(4.5))
    ))
```

The four clause types:

- **`must`** — has to match; **contributes to `_score`**.
- **`filter`** — has to match; does **not** affect `_score`, and results are cacheable. Use for yes/no conditions like brand or price range.
- **`should`** — not required, but matching **boosts `_score`**. If a `bool` has only `should` clauses (no `must`/`filter`), at least one must match.
- **`must_not`** — must **not** match; excluded entirely, no scoring impact.

This project's `SearchAsync` uses exactly this shape: the free-text `q`
goes in `must` (it should affect ranking), while `brand`/`price`/`rating`
go in `filter` (they narrow results but shouldn't change *how relevant* a
result is).

## 20. Filtering

See `filter` in [§19](#19-bool-query) above. The short version: if a condition is a hard
yes/no (does this product belong to brand Apple, yes or no?) put it in
`filter`. If a condition is about how *well* something matches (how
relevant is this text to "iphone"?), put it in `must`/`should`. Filters are
also cached by OpenSearch across repeated queries, which `must` clauses are
not, because filter results don't depend on a score that could change.

## 21. Relevance

"Relevance" is the answer to: *given many documents that all technically
match a query, which ones are the BEST matches, and in what order?*
OpenSearch's answer is a numeric `_score` computed per document, per query
— higher means "OpenSearch judged this a stronger match."

## 22. BM25

**Historically:** early full-text scoring used **TF-IDF** — term frequency
(how often does the term appear in this document) times inverse document
frequency (how rare is this term across all documents — rare terms are more
informative than common ones).

**Today:** OpenSearch's default is **BM25**, a refinement of the TF-IDF
idea that additionally accounts for:

- **Term frequency (TF)** — how many times the query term appears in the field, but with **diminishing returns** (the 10th occurrence of "iphone" barely adds more score than the 5th did — unlike raw TF-IDF, BM25 saturates).
- **Inverse document frequency (IDF)** — terms that appear in fewer documents overall score higher when matched (matching a rare word is more informative than matching a common one).
- **Field length normalization** — a match in a short field (a 3-word product name) scores higher than the same match diluted inside a long field (a 500-word description), because the term is a *larger fraction* of a short field.

BM25 is the default in OpenSearch/Lucene because it's a well-tested,
efficient formula that tends to produce better real-world ranking than
plain TF-IDF, without needing any training data or configuration.

**This project does not implement any of this math.** It only calls
OpenSearch's search API and reads back whatever `_score` OpenSearch
computed.

## 23. `_score`

Returned directly in the search response — see
[`Models/Dtos.cs`](Models/Dtos.cs) `SearchResultItem.Score`, populated from
`h.Score` in `ProductService.SearchAsync`. A document can have a higher
score than another because of any combination of: matching more query
terms, matching in a boosted field (`name^2` vs `description`), matching a
rarer term, or matching inside a shorter field. To see the exact breakdown
for one document, use the explain endpoint next.

## 24. Explain API

```http
GET /learning/explain/{productId}?q=iphone
```

Calls OpenSearch's real `_explain` API for one document against one query,
and returns the raw explanation OpenSearch itself produced — this project
does not reformat or simplify it.

```bash
curl "http://localhost:5000/learning/explain/2?q=iphone"
```

Raw OpenSearch DSL underneath (`GET /products-v1/_explain/2`):

```json
{
  "query": {
    "multi_match": { "query": "iphone", "fields": ["name^2", "description"] }
  }
}
```

The response nests explanations recursively — e.g. "weight of field
match" broken into "idf" and "tf" sub-explanations. Reading it top-down
usually looks like: total score = sum of per-clause scores, each clause
score = boost × idf × tf-based term saturation × length norm. This is the
best tool in the whole project for answering "why did this document score
what it scored."

## 25. Shards

```text
Index
  │
  ▼
Shard (one or more per index)
  │
  ▼
Lucene index (one per shard)
```

An OpenSearch **index** is a logical name (`products-v1`) that is
physically split into one or more **shards**. Each shard is, underneath,
an independent Lucene index with its own inverted index, term dictionary,
and segments.

- **Primary shard** — owns a portion of the index's documents; all writes for a document go to its primary shard first (determined by hashing the document id).
- **Replica shard** — a copy of a primary shard, kept in sync, living on a different node. Exists for redundancy (if the node holding the primary dies, a replica gets promoted) and for **read scaling** — replicas can serve search requests too, so more replicas can mean more search throughput.

**How search fans out:** a search request is sent to every shard that
holds part of the index (whichever copy — primary or replica — is chosen
to serve it), each shard searches its own local Lucene index independently
and returns its own top matches with scores, and the coordinating node
merges those per-shard results into one globally ranked list.

This project runs a **single-node** cluster (`discovery.type=single-node`
in `docker-compose.yml`) with the default 1 shard, 0 (or 1, cluster
default-dependent) replicas — deliberately not a multi-node cluster, so
there's nothing to debug operationally. But the concepts above are exactly
what a real multi-node cluster does; only the "different node" part is
missing here.

Observe:

```bash
curl http://localhost:9200/_cat/shards/products-v1?v
```

## 26. Replicas

Covered together with shards in [§25](#25-shards) above — a replica is a full copy of
a primary shard's Lucene index, kept up to date via the same
write/refresh/merge machinery described in [§27](#27-lucene)–[§30](#30-segment-merging), living so that
losing one node/copy doesn't lose data, and so read (search) load can be
spread across more than one copy.

## 27. Lucene

```text
OpenSearch
    │  distributed search engine: clustering, shards, replicas, REST API,
    │  cluster/index/document management
    ▼
Lucene
    │  the underlying Java search library that actually builds and
    │  searches the inverted index, on a single machine, for one shard
    ▼
Inverted Index
```

OpenSearch does not reimplement full-text search — each shard *is* a
Lucene index. OpenSearch's job is everything Lucene does not do on its
own: distributing shards across nodes, replicating them, routing requests,
merging per-shard results, exposing a REST API and Query DSL, security,
snapshots, and cluster management.

**This project is not implementing Lucene.** Every `_analyze`, `_search`,
and mapping decision in this codebase is this project asking OpenSearch to
do the work; OpenSearch asks Lucene.

## 28. Segments

A Lucene index (= one shard) is not one monolithic file — it's made of
**segments**: small, immutable, self-contained mini-indexes (each with its
own term dictionary, postings, and stored fields).

```text
Writes
  │
  ▼
New segment created
  │
  ▼
More writes → more segments
  │
  ▼
A search reads ALL segments and merges results
```

Segments are immutable once written — this is *why* updates and deletes
work the way they do (see [§31](#31-updates), [§32](#32-deletes)): you cannot edit a segment in
place, only write new segments and mark old data as superseded/deleted.

## 29. Refresh

**The question this section answers: why might a document you just indexed
not show up in a search immediately after?**

```text
Index document
      │
      ▼
Written to an in-memory buffer + a transaction log (durable, but not yet searchable)
      │
      ▼
Is it immediately searchable?  NO — not until a refresh happens
      │
      ▼
Refresh: the in-memory buffer is written out as a new, searchable Lucene segment
      │
      ▼
NOW searchable
```

By default, OpenSearch refreshes each index automatically about **once per
second**. That means a document indexed right now is typically searchable
within ~1 second, but not necessarily on the very next line of code — code
that does `Index(...)` immediately followed by `Search(...)` can miss the
document it just wrote.

**This project hits this directly in `SeedData.cs`:** after bulk-indexing
the seed products, it calls:

```csharp
await openSearch.Indices.RefreshAsync(OpenSearchIndexSetup.IndexName);
```

— an explicit, immediate refresh, specifically so the seed data is
searchable the instant the app finishes starting, instead of waiting for
the ~1s automatic timer. You can reproduce the "not yet searchable" gap
yourself in [Experiment 5](LEARNING.md).

## 30. Segment merging

More writes → more segments → eventually too many small segments, which
makes search slower (every search has to check every segment) and wastes
space (deleted documents still occupy their old segment until merged
away).

```text
Many small segments
      │
      ▼
Lucene merges several segments into one larger segment in the background
      │
      ▼
Documents marked deleted in the old segments are physically dropped during the merge
      │
      ▼
Fewer, larger segments — faster search, reclaimed space
```

This project never triggers or configures merging — it is entirely
automatic, background behavior in Lucene. It's included here purely so you
know *why* a delete doesn't instantly shrink the index on disk (see
[§32](#32-deletes)).

## 31. Updates

```http
PUT /products-v1/_doc/{id}
```

There is no in-place field mutation the way `UPDATE products SET price =
... WHERE id = ...` works in PostgreSQL. Because segments are immutable
([§28](#28-segments)), an "update" via `IndexAsync` with an existing id is really:

```text
1. The old document (same id) is marked deleted in its current segment
2. The new document body is indexed as a brand-new document, in a new/current segment
3. Both facts get reconciled during the next refresh/merge
```

This project's `ProductService.UpdateAsync` calls the exact same
`IndexAsync` used for creation — same id, entirely new document body. This
is a **full replacement**, not a partial patch: any field not included in
the new document body would be gone, because the whole old document is
superseded, not merged field-by-field. (OpenSearch does separately offer a
partial "update" API — `POST /_update/{id}` with a `doc` fragment — that
merges fields under the hood by re-indexing the merged result; this
project doesn't use it, to keep exactly one code path for "put a document
version into the index.")

**What happens if PostgreSQL changes but OpenSearch is not updated?** The
two stores drift — OpenSearch keeps serving search results based on the
stale document until something re-indexes it. This project has no
mechanism to detect or repair that drift automatically (see [§36](#36-data-synchronization)) —
every write path in this codebase updates both stores in the same request,
but if that request fails between the two writes, or if some other process
edits PostgreSQL directly, drift is possible and this project does not
guard against it.

## 32. Deletes

```http
DELETE /products-v1/_doc/{id}
```

Deletion is **not** "erase bytes from disk right now." Because segments
are immutable, a delete just writes a **tombstone** — a marker saying "the
document at this position in this segment is deleted." The document's
bytes are still physically present in that segment until it is later
picked up by [segment merging](#30-segment-merging), at which point tombstoned
documents are dropped and not copied into the merged segment. Between the
delete and the next relevant merge, the space isn't reclaimed, and (until
the next refresh) the document may briefly still be visible to an
in-flight search that started just before the delete.

## 33. `_source`

Every indexed document has its original JSON body stored verbatim as
`_source` — this is what gets returned when you fetch or search a
document. It is **not** the same thing as the inverted index:

```text
_source (stored document)          ≠         Inverted index (search structures)
─────────────────────────────                ─────────────────────────────────
The exact JSON you sent,                      Tokens → postings lists,
kept for retrieval/reindexing                 term dictionaries, doc values —
                                               built FROM _source, used to
                                               find matching documents
```

`_source` is what `ProductService.SearchAsync` reads via `h.Source` to
build the API response — it is a stored copy, not a query against the
inverted index. If you disabled `_source` storage (possible, not done
here), search would still work, but you could no longer retrieve the
original document body, and reindexing into a new index would become
impossible without keeping the data elsewhere (which, in this project, is
exactly PostgreSQL's job).

## 34. Reindexing

Recall from [§7](#7-mapping): a field's *type* generally can't be changed once
documents exist under a mapping. The fix is a new index with the corrected
mapping, populated from the old one:

```text
products-v1  (old mapping)
      │
      │  reindex
      ▼
products-v2  (new mapping)
```

Concretely, if you decided `category` should become a `text` field (to
support fuzzy category search) instead of `keyword`:

```bash
# 1. Create the new index with the new mapping
curl -X PUT "http://localhost:9200/products-v2" -H "Content-Type: application/json" -d '{
  "mappings": {
    "properties": {
      "name":        { "type": "text" },
      "description": { "type": "text" },
      "category":    { "type": "text" },
      "brand":       { "type": "keyword" },
      "price":       { "type": "double" },
      "rating":      { "type": "double" },
      "popularity":  { "type": "integer" }
    }
  }
}'

# 2. Copy documents from the old index into the new one, using OpenSearch's own reindex API
curl -X POST "http://localhost:9200/_reindex" -H "Content-Type: application/json" -d '{
  "source": { "index": "products-v1" },
  "dest":   { "index": "products-v2" }
}'

# 3. Point the application at products-v2 (in this project: OpenSearchIndexSetup.IndexName)
```

This project does not build alias-swapping or zero-downtime deployment
machinery — that's a real production concern (typically: write to both
indices during a migration window, or use an index alias that gets
atomically repointed). Here, the goal is just to understand *why*
reindexing exists, not to build a deployment pipeline for it.

## 35. PostgreSQL vs OpenSearch

| PostgreSQL | OpenSearch |
|---|---|
| Source of truth | Search/read model |
| Tables | Indexes |
| Rows | Documents |
| B-tree indexes | Inverted index / Lucene structures |
| SQL | Query DSL (JSON) |
| Transactional database | Search engine |

We keep the product in **both** because they solve different problems:
PostgreSQL guarantees the data is correct and durable; OpenSearch makes
that data fast and relevant to search over. Losing OpenSearch's index is
recoverable (rebuild from PostgreSQL); losing PostgreSQL is not (OpenSearch
was never meant to be the only copy).

## 36. Data synchronization

Deliberately the simplest possible approach — no queue, no events, no
background worker:

```text
POST /products        →  PostgreSQL insert  →  OpenSearch index
PUT  /products/{id}    →  PostgreSQL update  →  OpenSearch re-index (full replace)
DELETE /products/{id}  →  PostgreSQL delete  →  OpenSearch delete
```

All of this happens synchronously, in the same HTTP request, in
`ProductService`. The tradeoff is explicit: if the process crashes after
the PostgreSQL write but before the OpenSearch write, the two stores drift
until something notices and repairs it (see [§31](#31-updates)).

**In a real production system**, this synchronization is usually made
asynchronous and durable — e.g. writing to PostgreSQL, then publishing a
change event (via an outbox table, CDC/Debezium, or a message broker) that
a separate consumer uses to update OpenSearch, decoupling the two writes
and making retries/replay possible. That is a distributed-systems problem
in its own right, and out of scope here on purpose (see [§38](#38-things-this-project-deliberately-does-not-do)) — this
project exists to teach OpenSearch, not eventual consistency patterns.

## 37. Learning experiments

See [`LEARNING.md`](LEARNING.md) for ten hands-on experiments to run against this
project, each pointing back at the relevant section above.

## 38. Things this project deliberately does not do

Authentication, authorization, payments, users, orders, cart, inventory,
Kafka, RabbitMQ, Redis, Kubernetes, CI/CD, cloud deployment, a monitoring
stack, Grafana/Prometheus, distributed tracing, Elasticsearch, OpenSearch
operators, a frontend framework, microservices, CQRS, MediatR, event
sourcing, generic repository/unit-of-work abstractions, or DDD. If you find
yourself wanting one of these, that's a sign to fork this into a different,
non-learning project — not to add it here.
