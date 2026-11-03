namespace Ta3lom.Store.Infrastructure.Persistence.Records;

/// Table `document_counter` (F-D4): the next receipt number per series and Riyadh year. Only `DocumentNumbers` touches it,
/// with an upsert that row-locks the counter inside the settling transaction, so a rolled-back settlement leaves no gap.
internal sealed class DocumentCounterRecord
{
    private DocumentCounterRecord()
    {
    }

    public string Series { get; private set; } = null!;

    public int Year { get; private set; }

    public int NextNumber { get; private set; }
}
