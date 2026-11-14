using System.Collections.ObjectModel;

namespace PoliClick;

public sealed class QueueBoard
{
    private readonly Dictionary<string, string> calls = [];

    public string Id { get; }
    public IReadOnlyList<string> CabinetNumbers { get; }
    public IReadOnlyDictionary<string, string> Calls => new ReadOnlyDictionary<string, string>(calls);

    internal QueueBoard(string id, IEnumerable<string> cabinetNumbers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(cabinetNumbers);
        var cabinets = cabinetNumbers.Select(number =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(number);
            return number.Trim();
        }).ToList();
        if (cabinets.Count == 0 || cabinets.Distinct().Count() != cabinets.Count)
            throw new ArgumentException("Укажите кабинеты без повторов.", nameof(cabinetNumbers));

        Id = id.Trim();
        CabinetNumbers = cabinets.AsReadOnly();
    }

    internal void ShowCall(string cabinetNumber, string appointmentNumber)
    {
        if (!CabinetNumbers.Contains(cabinetNumber))
            throw new InvalidOperationException("Табло не обслуживает этот кабинет.");

        calls[cabinetNumber] = appointmentNumber;
    }

    internal void Clear() => calls.Clear();
}
