namespace PoliClick;

public sealed class SlotStorage
{
    private readonly List<TimeSlot> slots = [];

    public IReadOnlyList<TimeSlot> Slots => slots.AsReadOnly();

    internal IReadOnlyList<TimeSlot> CreateSlots(Doctor doctor, string cabinetNumber,
        DateTime start, DateTime end)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cabinetNumber);
        cabinetNumber = cabinetNumber.Trim();
        if (start.Date != end.Date || start >= end)
            throw new ArgumentException("Интервал работы должен начинаться и заканчиваться в один день.");

        var duration = TimeSpan.FromMinutes(doctor.SlotMinutes);
        if ((end - start).Ticks % duration.Ticks != 0)
            throw new ArgumentException("Интервал работы должен делиться на длительность слота без остатка.");

        // проверяем весь интервал до добавления, при ошибке расписание не меняется
        if (slots.Any(s => start < s.End && end > s.Start &&
            (s.Doctor.Id == doctor.Id || s.CabinetNumber == cabinetNumber)))
        {
            throw new InvalidOperationException("Врач или кабинет уже занят в этом интервале.");
        }

        var created = new List<TimeSlot>();
        for (var time = start; time < end; time += duration)
        {
            var slot = new TimeSlot(slots.Count + 1, doctor, cabinetNumber, time, time + duration);
            slots.Add(slot);
            created.Add(slot);
        }

        return created.AsReadOnly();
    }

    internal TimeSlot GetSlot(int slotId) => slots.FirstOrDefault(s => s.Id == slotId)
        ?? throw new KeyNotFoundException("Слот не найден.");

    public IReadOnlyList<TimeSlot> FindFreeSlots(string specialty, DateOnly date, int? doctorId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(specialty);
        return slots.Where(s => s.IsFree && DateOnly.FromDateTime(s.Start) == date &&
                string.Equals(s.Doctor.Specialty, specialty.Trim(), StringComparison.OrdinalIgnoreCase) &&
                (doctorId is null || s.Doctor.Id == doctorId))
            .OrderBy(s => s.Start).ThenBy(s => s.Doctor.FullName)
            .ToList().AsReadOnly();
    }
}
