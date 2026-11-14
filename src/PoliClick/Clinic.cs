namespace PoliClick;

// lock пока нужен только для демонстрации параллельных запросов
// для последовательной консоли он излишен
public sealed class Clinic
{
    private readonly PatientStorage patientStorage = new();
    private readonly DoctorStorage doctorStorage = new();
    private readonly List<Appointment> appointments = [];
    private readonly List<QueueBoard> queueBoards = [];
    private readonly Func<DateTime> now;
    private readonly object bookingLock = new();

    public SlotStorage Schedule { get; } = new();
    public IReadOnlyList<Patient> Patients => patientStorage.Patients;
    public IReadOnlyList<Doctor> Doctors => doctorStorage.Doctors;
    public IReadOnlyList<QueueBoard> QueueBoards => queueBoards.AsReadOnly();

    // подмена времени нужна для демонстрации и проверок без реального ожидания
    public Clinic(Func<DateTime>? now = null) => this.now = now ?? (() => DateTime.Now);

    public Patient AddPatient(string fullName, DateOnly birthDate, string policyNumber, bool isAttached = true)
    {
        if (birthDate > DateOnly.FromDateTime(now()))
            throw new ArgumentException("Дата рождения не может быть в будущем.", nameof(birthDate));
        return patientStorage.AddPatient(fullName, birthDate, policyNumber, isAttached);
    }

    public Doctor AddDoctor(string fullName, string specialty, int slotMinutes) =>
        doctorStorage.AddDoctor(fullName, specialty, slotMinutes);

    public IReadOnlyList<string> GetSpecialties() => doctorStorage.GetSpecialties();

    public IReadOnlyList<Doctor> GetDoctors(string specialty) => doctorStorage.GetDoctors(specialty);

    public IReadOnlyList<DateOnly> GetFreeDates(string specialty, int? doctorId = null)
    {
        lock (bookingLock)
        {
            var currentTime = now();
            var lastDate = DateOnly.FromDateTime(currentTime).AddDays(14);
            return Schedule.GetFreeDates(specialty, currentTime, lastDate, doctorId);
        }
    }

    public IReadOnlyList<TimeSlot> CreateSchedule(int doctorId, string cabinetNumber, DateTime start, DateTime end)
    {
        lock (bookingLock)
        {
            var doctor = doctorStorage.GetDoctor(doctorId);
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
            var patient = patientStorage.GetPatient(patientId);
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

    public Appointment StartAppointment(int doctorId, int appointmentId)
    {
        lock (bookingLock)
        {
            var appointment = OpenAppointment(doctorId, appointmentId);
            if (appointment.Status != AppointmentStatus.PLANNED)
                throw new InvalidOperationException("Начать можно только запланированный прием.");

            var currentTime = now();
            if (appointment.Slot.Start.Date != currentTime.Date)
                throw new InvalidOperationException("Прием можно начать только в назначенный день.");

            var occupied = appointments.Where(a => a.Id != appointmentId && OccupiesQueueResources(a, currentTime));
            if (occupied.Any(a => a.Doctor.Id == doctorId))
                throw new InvalidOperationException("Врач ведет другой прием или ожидает вызванного пациента.");
            if (occupied.Any(a => a.Patient.Id == appointment.Patient.Id))
                throw new InvalidOperationException("Пациент участвует в другом приеме или уже вызван в другой кабинет.");
            if (occupied.Any(a => a.CabinetNumber == appointment.CabinetNumber))
                throw new InvalidOperationException("Кабинет занят приемом или ожиданием вызванного пациента.");

            appointment.Start(currentTime);
            return appointment;
        }
    }

    public void CancelAppointment(int patientId, int appointmentId)
    {
        lock (bookingLock)
        {
            var appointment = patientStorage.GetPatient(patientId).Appointments.FirstOrDefault(a => a.Id == appointmentId)
                ?? throw new KeyNotFoundException("У пациента нет такой записи.");
            if (appointment.Slot.Start - now() < TimeSpan.FromHours(1))
                throw new InvalidOperationException("Менее чем за час до приема отмена доступна только через регистратуру.");

            appointment.Cancel();
        }
    }

    public QueueBoard AddQueueBoard(string id, params string[] cabinetNumbers)
    {
        lock (bookingLock)
        {
            var board = new QueueBoard(id, cabinetNumbers);
            if (queueBoards.Any(b => b.Id == board.Id))
                throw new InvalidOperationException("Табло с таким идентификатором уже есть.");
            if (queueBoards.Any(b => b.CabinetNumbers.Intersect(board.CabinetNumbers).Any()))
                throw new InvalidOperationException("Кабинет уже относится к другому табло.");

            queueBoards.Add(board);
            return board;
        }
    }

    public IReadOnlyList<Appointment> UpdateQueue()
    {
        lock (bookingLock)
        {
            var currentTime = now();
            foreach (var board in queueBoards)
                board.Clear();

            var occupied = appointments.Where(a => OccupiesQueueResources(a, currentTime)).ToList();
            var busyDoctors = occupied.Select(a => a.Doctor.Id).ToHashSet();
            var busyPatients = occupied.Select(a => a.Patient.Id).ToHashSet();
            var busyCabinets = occupied.Select(a => a.CabinetNumber).ToHashSet();

            // вызов остается на табло, пока пациент не начал прием или врач не отметил неявку
            foreach (var waiting in occupied.Where(a => a.Status == AppointmentStatus.PLANNED))
            {
                var board = queueBoards.FirstOrDefault(b => b.CabinetNumbers.Contains(waiting.CabinetNumber));
                board?.ShowCall(waiting.CabinetNumber, waiting.Number);
            }

            var called = new List<Appointment>();
            var candidates = appointments.Where(a => a.Status == AppointmentStatus.PLANNED &&
                    a.CalledAt is null && a.Slot.Start.Date == currentTime.Date && a.Slot.Start <= currentTime)
                .OrderBy(a => a.Slot.Start).ThenBy(a => a.Id);

            foreach (var appointment in candidates)
            {
                var board = queueBoards.FirstOrDefault(b => b.CabinetNumbers.Contains(appointment.CabinetNumber));
                if (board is null || busyDoctors.Contains(appointment.Doctor.Id) ||
                    busyPatients.Contains(appointment.Patient.Id) || busyCabinets.Contains(appointment.CabinetNumber))
                    continue;

                appointment.Call(currentTime);
                board.ShowCall(appointment.CabinetNumber, appointment.Number);
                busyDoctors.Add(appointment.Doctor.Id);
                busyPatients.Add(appointment.Patient.Id);
                busyCabinets.Add(appointment.CabinetNumber);
                called.Add(appointment);
            }

            return called.AsReadOnly();
        }
    }

    public void MarkNoShow(int doctorId, int appointmentId)
    {
        lock (bookingLock)
        {
            var appointment = OpenAppointment(doctorId, appointmentId);
            appointment.MarkNoShow(now());
        }
    }

    private static bool OccupiesQueueResources(Appointment appointment, DateTime currentTime) =>
        appointment.Status == AppointmentStatus.IN_PROGRESS ||
        (appointment.Status == AppointmentStatus.PLANNED && appointment.CalledAt is not null &&
         appointment.Slot.Start.Date == currentTime.Date);
}
