namespace PoliClick;

// lock пока нужен только для демонстрации параллельных запросов
// для последовательной консоли он излишен
public sealed class Clinic
{
    private readonly List<Patient> patients = [];
    private readonly List<Doctor> doctors = [];
    private readonly List<Appointment> appointments = [];
    private readonly Func<DateTime> now;
    private readonly object bookingLock = new();

    public SlotStorage Schedule { get; } = new();
    public IReadOnlyList<Patient> Patients => patients.AsReadOnly();
    public IReadOnlyList<Doctor> Doctors => doctors.AsReadOnly();

    // подмена времени нужна для демонстрации и проверок без реального ожидания
    public Clinic(Func<DateTime>? now = null) => this.now = now ?? (() => DateTime.Now);

    public Patient AddPatient(string fullName, DateOnly birthDate, string policyNumber, bool isAttached = true)
    {
        if (birthDate > DateOnly.FromDateTime(now()))
            throw new ArgumentException("Дата рождения не может быть в будущем.", nameof(birthDate));
        if (patients.Any(p => p.PolicyNumber == policyNumber))
            throw new InvalidOperationException("Пациент с таким полисом уже есть.");

        var patient = new Patient(patients.Count + 1, fullName, birthDate, policyNumber, isAttached);
        patients.Add(patient);
        return patient;
    }

    public Doctor AddDoctor(string fullName, string specialty, int slotMinutes)
    {
        var doctor = new Doctor(doctors.Count + 1, fullName, specialty, slotMinutes);
        if (doctors.Any(d => string.Equals(d.Specialty, doctor.Specialty, StringComparison.OrdinalIgnoreCase) &&
            d.SlotMinutes != doctor.SlotMinutes))
        {
            throw new InvalidOperationException("Длительность слота должна быть одинаковой для врачей одной специальности.");
        }

        doctors.Add(doctor);
        return doctor;
    }

    public IReadOnlyList<TimeSlot> CreateSchedule(int doctorId, string cabinetNumber, DateTime start, DateTime end)
    {
        lock (bookingLock)
        {
            var doctor = doctors.FirstOrDefault(d => d.Id == doctorId)
                ?? throw new KeyNotFoundException("Врач не найден.");
            if (start <= now())
                throw new ArgumentException("Расписание создается на будущее время.", nameof(start));

            return Schedule.CreateSlots(doctor, cabinetNumber, start, end);
        }
    }

    public IReadOnlyList<TimeSlot> FindFreeSlots(string specialty, DateOnly date, int? doctorId = null)
    {
        lock (bookingLock)
        {
            var currentTime = now();
            var lastDate = DateOnly.FromDateTime(currentTime).AddDays(14);
            if (date < DateOnly.FromDateTime(currentTime) || date > lastDate)
                return Array.Empty<TimeSlot>();

            return Schedule.FindFreeSlots(specialty, date, doctorId)
                .Where(s => s.Start > currentTime).ToList().AsReadOnly();
        }
    }

    public Appointment BookAppointment(int patientId, int slotId)
    {
        // одна блокировка защищает и слот, и ограничение на запись к специальности
        lock (bookingLock)
        {
            var patient = GetPatient(patientId);
            var slot = Schedule.GetSlot(slotId);
            var currentTime = now();
            if (!patient.IsAttached)
                throw new InvalidOperationException("Для самостоятельной записи нужно прикрепление к поликлинике.");
            if (slot.Start <= currentTime ||
                DateOnly.FromDateTime(slot.Start) > DateOnly.FromDateTime(currentTime).AddDays(14))
                throw new InvalidOperationException("Выберите будущее время в пределах 14 дней.");
            if (!slot.IsFree)
                throw new InvalidOperationException("Это время уже занято. Выберите другой слот.");
            if (patient.HasActiveAppointment(slot.Doctor.Specialty))
                throw new InvalidOperationException("У пациента уже есть активная запись к этой специальности.");

            var appointment = new Appointment(appointments.Count + 1, patient, slot, now);
            slot.Reserve(appointment);
            patient.AddAppointment(appointment);
            appointments.Add(appointment);
            return appointment;
        }
    }

    public Appointment OpenAppointment(int doctorId, int appointmentId)
    {
        var appointment = appointments.FirstOrDefault(a => a.Id == appointmentId)
            ?? throw new KeyNotFoundException("Запись на прием не найдена.");
        if (appointment.Doctor.Id != doctorId)
            throw new InvalidOperationException("Эту запись может открыть только назначенный врач.");

        return appointment;
    }

    public void CancelAppointment(int patientId, int appointmentId)
    {
        lock (bookingLock)
        {
            var appointment = GetPatient(patientId).Appointments.FirstOrDefault(a => a.Id == appointmentId)
                ?? throw new KeyNotFoundException("У пациента нет такой записи.");
            if (appointment.Slot.Start - now() < TimeSpan.FromHours(1))
                throw new InvalidOperationException("Менее чем за час до приема отмена доступна только через регистратуру.");

            appointment.Cancel();
        }
    }

    private Patient GetPatient(int patientId) => patients.FirstOrDefault(p => p.Id == patientId)
        ?? throw new KeyNotFoundException("Пациент не найден.");
}
