namespace PoliClick;

public sealed class TimeSlot
{
    private Appointment? reservation;

    public int Id { get; }
    public Doctor Doctor { get; }
    public string CabinetNumber { get; }
    public DateTime Start { get; }
    public DateTime End { get; }
    public bool IsFree => reservation is null;

    internal TimeSlot(int id, Doctor doctor, string cabinetNumber, DateTime start, DateTime end)
    {
        Id = id;
        Doctor = doctor;
        CabinetNumber = cabinetNumber;
        Start = start;
        End = end;
    }

    // бронирование и освобождение вызываются Clinic внутри одной блокировки
    internal void Reserve(Appointment appointment)
    {
        if (!IsFree)
            throw new InvalidOperationException("Это время уже занято. Выберите другой слот.");

        reservation = appointment;
    }

    internal void Release(Appointment appointment)
    {
        if (!ReferenceEquals(reservation, appointment))
            throw new InvalidOperationException("Слот занят другой записью.");

        reservation = null;
    }
}
