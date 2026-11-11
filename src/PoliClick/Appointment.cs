using System.Text.RegularExpressions;

namespace PoliClick;

public enum AppointmentStatus
{
    PLANNED,
    IN_PROGRESS,
    COMPLETED,
    CANCELLED
}

public sealed class Appointment
{
    private readonly Func<DateTime> now;
    private readonly List<string> prescriptions = [];

    public int Id { get; }
    public string Number => $"A{Id:D4}";
    public Patient Patient { get; }
    public Doctor Doctor { get; }
    public string CabinetNumber { get; }
    public TimeSlot Slot { get; }
    public AppointmentStatus Status { get; private set; } = AppointmentStatus.PLANNED;
    public bool IsActive => Status is AppointmentStatus.PLANNED or AppointmentStatus.IN_PROGRESS;
    public DateTime? StartedAt { get; private set; }
    public DateTime? FinishedAt { get; private set; }
    public string Complaints { get; private set; } = "";
    public string Anamnesis { get; private set; } = "";
    public string Examination { get; private set; } = "";
    public string DiagnosisCode { get; private set; } = "";
    public IReadOnlyList<string> Prescriptions => prescriptions.AsReadOnly();

    internal Appointment(int id, Patient patient, TimeSlot slot, Func<DateTime> now)
    {
        Id = id;
        Patient = patient;
        Slot = slot;
        Doctor = slot.Doctor;
        CabinetNumber = slot.CabinetNumber;
        this.now = now;
    }

    public void Start()
    {
        if (Status != AppointmentStatus.PLANNED)
            throw new InvalidOperationException("Начать можно только запланированный прием.");

        if (Patient.Appointments.Any(a => a.Status == AppointmentStatus.IN_PROGRESS))
            throw new InvalidOperationException("У пациента уже идет другой прием.");

        StartedAt = now();
        Status = AppointmentStatus.IN_PROGRESS;
    }

    public void SetExamination(string complaints, string anamnesis, string examination)
    {
        EnsureInProgress();
        ArgumentException.ThrowIfNullOrWhiteSpace(complaints);
        ArgumentException.ThrowIfNullOrWhiteSpace(examination);

        Complaints = complaints.Trim();
        Anamnesis = anamnesis?.Trim() ?? "";
        Examination = examination.Trim();
    }

    public void SetDiagnosis(string code)
    {
        EnsureInProgress();
        code = code?.Trim().ToUpperInvariant() ?? "";
        if (!Regex.IsMatch(code, @"\A[A-Z][0-9]{2}(\.[0-9])?\z"))
            throw new ArgumentException("Укажите код МКБ-10, например J06.9.", nameof(code));

        DiagnosisCode = code;
    }

    public void AddPrescription(string prescription)
    {
        EnsureInProgress();
        ArgumentException.ThrowIfNullOrWhiteSpace(prescription);
        prescriptions.Add(prescription.Trim());
    }

    public void Complete()
    {
        EnsureInProgress();
        if (string.IsNullOrWhiteSpace(Complaints) || string.IsNullOrWhiteSpace(Examination) ||
            string.IsNullOrWhiteSpace(DiagnosisCode))
        {
            throw new InvalidOperationException("Для завершения нужны жалобы, осмотр и диагноз.");
        }

        var finishedAt = now();
        if (finishedAt < StartedAt)
            throw new InvalidOperationException("Время завершения не может быть раньше начала приема.");

        FinishedAt = finishedAt;
        Status = AppointmentStatus.COMPLETED;
    }

    internal void Cancel()
    {
        if (Status != AppointmentStatus.PLANNED)
            throw new InvalidOperationException("Отменить можно только запланированную запись.");

        Slot.Release(this);
        Status = AppointmentStatus.CANCELLED;
    }

    private void EnsureInProgress()
    {
        if (Status != AppointmentStatus.IN_PROGRESS)
            throw new InvalidOperationException("Менять данные можно только во время незавершенного приема.");
    }
}
