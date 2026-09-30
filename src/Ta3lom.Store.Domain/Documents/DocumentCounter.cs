namespace Ta3lom.Store.Domain.Documents;

/// Table `document_counter` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class DocumentCounter
{
    public DocumentCounter(DocumentSeries series, int year)
    {
        Series = series;
        Year = year;
        LastValue = 0;
    }

    private DocumentCounter()
    {
    }

    public DocumentSeries Series { get; private set; }

    public int Year { get; private set; }

    public int LastValue { get; private set; }
}
