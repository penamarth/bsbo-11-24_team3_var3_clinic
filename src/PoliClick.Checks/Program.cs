using System.Collections.Concurrent;
using System.Text;
using PoliClick;

Console.OutputEncoding = Encoding.UTF8;
var failed = 0;
var passed = 0;

Run("Нарезка рабочего интервала", () =>
{
    var (clinic, doctor, _, _, day) = CreateScenario();
    var slots = clinic.Schedule.Slots;
    Check(slots.Count == 3, "Ожидалось три слота по 20 минут.");
    Check(slots.All(s => s.Doctor == doctor && s.CabinetNumber == "101"), "Потеряны врач или кабинет.");
    Check(slots[0].Start == day && slots[^1].End == day.AddHours(1), "Неверные границы.");
});

Run("Длительность слота одинакова для одной специальности", () =>
{
    var (clinic, _, _, _, _) = CreateScenario();
    Expect<InvalidOperationException>(() => clinic.AddDoctor("Другой врач", "терапия", 30));
    Expect<ArgumentOutOfRangeException>(() => clinic.AddDoctor("Другой врач", "Хирургия", 0));
    Check(clinic.Doctors.Count == 1, "Некорректный врач добавлен.");
});

Run("Пересечение расписания врача отклоняется без частичного добавления", () =>
{
    var (clinic, doctor, _, _, day) = CreateScenario();
    Expect<InvalidOperationException>(() => clinic.CreateSchedule(doctor.Id, "102", day.AddMinutes(20), day.AddMinutes(80)));
    Check(clinic.Schedule.Slots.Count == 3, "После ошибки добавились слоты.");
});

Run("Пересечение расписания кабинета отклоняется", () =>
{
    var (clinic, _, _, _, day) = CreateScenario();
    var doctor = clinic.AddDoctor("Другой врач", "Хирургия", 20);
    Expect<InvalidOperationException>(() => clinic.CreateSchedule(doctor.Id, "101", day, day.AddHours(1)));
    Check(clinic.Schedule.Slots.Count == 3, "Расписание изменилось.");
});

Run("Соседние интервалы и разные кабинеты допустимы", () =>
{
    var (clinic, doctor, _, _, day) = CreateScenario();
    clinic.CreateSchedule(doctor.Id, "101", day.AddHours(1), day.AddHours(2));
    var anotherDoctor = clinic.AddDoctor("Другой врач", "Хирургия", 20);
    clinic.CreateSchedule(anotherDoctor.Id, "102", day, day.AddHours(1));
    Check(clinic.Schedule.Slots.Count == 9, "Не созданы независимые слоты.");
});

Run("Неполный, пустой и переходящий через полночь интервалы отклоняются", () =>
{
    var (clinic, doctor, _, _, day) = CreateScenario();
    Expect<ArgumentException>(() => clinic.CreateSchedule(doctor.Id, "102", day.AddDays(1), day.AddDays(1).AddMinutes(25)));
    Expect<ArgumentException>(() => clinic.CreateSchedule(doctor.Id, "102", day, day));
    Expect<ArgumentException>(() => clinic.CreateSchedule(doctor.Id, "102", day.Date.AddHours(23), day.Date.AddDays(1).AddHours(1)));
    Check(clinic.Schedule.Slots.Count == 3, "Плохой интервал изменил расписание.");
});

Run("В запись копируются врач и кабинет, у пациента сохраняется тот же объект", () =>
{
    var (clinic, doctor, patient, _, _) = CreateScenario();
    var appointment = clinic.BookAppointment(patient.Id, 1);
    Check(appointment.Doctor == doctor && appointment.Patient == patient && appointment.CabinetNumber == "101", "Потеряны ссылки.");
    Check(ReferenceEquals(patient.Appointments[0], clinic.OpenAppointment(doctor.Id, appointment.Id)), "Создана копия приема.");
    Check(!clinic.Schedule.Slots[0].IsFree, "Слот остался свободным.");
});

Run("Повторная запись другого пациента на занятый слот отклоняется", () =>
{
    var (clinic, _, patient, anotherPatient, _) = CreateScenario();
    clinic.BookAppointment(patient.Id, 1);
    Expect<InvalidOperationException>(() => clinic.BookAppointment(anotherPatient.Id, 1));
    Check(anotherPatient.Appointments.Count == 0, "Появилась лишняя запись.");
});

Run("Вторая активная запись к той же специальности отклоняется", () =>
{
    var (clinic, _, patient, _, _) = CreateScenario();
    clinic.BookAppointment(patient.Id, 1);
    Expect<InvalidOperationException>(() => clinic.BookAppointment(patient.Id, 2));
    Check(clinic.Schedule.Slots[1].IsFree, "Неуспешная запись заняла слот.");
});

Run("Запись без прикрепления отклоняется", () =>
{
    var (clinic, _, _, _, _) = CreateScenario();
    var patient = clinic.AddPatient("Без прикрепления", new DateOnly(1990, 1, 1), "0000000000000003", false);
    Expect<InvalidOperationException>(() => clinic.BookAppointment(patient.Id, 1));
    Check(clinic.Schedule.Slots[0].IsFree, "Неуспешная запись заняла слот.");
});

Run("Горизонт записи и прошедшие слоты проверяются при поиске и записи", () =>
{
    var clock = new DateTime(2030, 1, 10, 8, 0, 0);
    var clinic = new Clinic(() => clock);
    var doctor = clinic.AddDoctor("Врач", "Терапия", 20);
    var patient = clinic.AddPatient("Пациент", new DateOnly(1990, 1, 1), "0000000000000001");
    var last = clock.Date.AddDays(14).AddHours(23);
    var allowed = clinic.CreateSchedule(doctor.Id, "101", last, last.AddMinutes(20))[0];
    var outside = clinic.CreateSchedule(doctor.Id, "101", last.AddDays(1), last.AddDays(1).AddMinutes(20))[0];
    Check(clinic.FindFreeSlots("Терапия", DateOnly.FromDateTime(last)).Count == 1, "Последний день недоступен.");
    Check(clinic.FindFreeSlots("Терапия", DateOnly.FromDateTime(last.AddDays(1))).Count == 0, "Горизонт поиска превышен.");
    Expect<InvalidOperationException>(() => clinic.BookAppointment(patient.Id, outside.Id));
    clock = allowed.Start;
    Check(clinic.FindFreeSlots("Терапия", DateOnly.FromDateTime(last)).Count == 0, "Прошедшее время найдено.");
    Expect<InvalidOperationException>(() => clinic.BookAppointment(patient.Id, allowed.Id));
});

Run("Отмена сохраняет историю записей и освобождает слот", () =>
{
    var (clinic, _, patient, anotherPatient, _) = CreateScenario();
    var cancelled = clinic.BookAppointment(patient.Id, 1);
    clinic.CancelAppointment(patient.Id, cancelled.Id);
    var replacement = clinic.BookAppointment(anotherPatient.Id, 1);
    Check(cancelled.Status == AppointmentStatus.CANCELLED, "Статус не изменился.");
    Check(patient.Appointments.Contains(cancelled) && replacement.Id != cancelled.Id, "История потеряна.");
});

Run("За час отменять можно, менее чем за час нельзя", () =>
{
    var clock = new DateTime(2030, 1, 10, 8, 0, 0);
    var clinic = new Clinic(() => clock);
    var doctor = clinic.AddDoctor("Врач", "Терапия", 20);
    var patient = clinic.AddPatient("Пациент", new DateOnly(1990, 1, 1), "0000000000000001");
    clinic.CreateSchedule(doctor.Id, "101", clock.AddHours(1), clock.AddHours(2));
    var appointment = clinic.BookAppointment(patient.Id, 1);
    clinic.CancelAppointment(patient.Id, appointment.Id);
    var newAppointment = clinic.BookAppointment(patient.Id, 1);
    clock = clock.AddSeconds(1);
    Expect<InvalidOperationException>(() => clinic.CancelAppointment(patient.Id, newAppointment.Id));
    Check(newAppointment.Status == AppointmentStatus.PLANNED, "Неуспешная отмена изменила запись.");
});

Run("Чужую запись нельзя отменить или открыть от имени другого врача", () =>
{
    var (clinic, _, patient, anotherPatient, _) = CreateScenario();
    var appointment = clinic.BookAppointment(patient.Id, 1);
    var doctor = clinic.AddDoctor("Другой врач", "Хирургия", 20);
    Expect<InvalidOperationException>(() => clinic.OpenAppointment(doctor.Id, appointment.Id));
    Expect<KeyNotFoundException>(() => clinic.CancelAppointment(anotherPatient.Id, appointment.Id));
    Check(appointment.Status == AppointmentStatus.PLANNED, "Запись изменилась.");
});

Run("Прием требует начала, жалоб, осмотра и диагноза", () =>
{
    var (clinic, _, patient, _, _) = CreateScenario();
    var appointment = clinic.BookAppointment(patient.Id, 1);
    Expect<InvalidOperationException>(() => appointment.SetDiagnosis("R69"));
    Expect<InvalidOperationException>(() => appointment.Complete());
    appointment.Start();
    Expect<InvalidOperationException>(() => appointment.Start());
    Expect<InvalidOperationException>(() => appointment.Complete());
    appointment.SetExamination("Жалобы", "", "Осмотр");
    Expect<InvalidOperationException>(() => appointment.Complete());
    Expect<ArgumentException>(() => appointment.SetDiagnosis("нет кода"));
    appointment.SetDiagnosis("j06.9");
    Check(appointment.DiagnosisCode == "J06.9", "Не нормализован код.");
});

Run("Черновик не входит в историю, завершенный прием нельзя менять", () =>
{
    var (clinic, _, patient, _, _) = CreateScenario();
    var appointment = clinic.BookAppointment(patient.Id, 1);
    appointment.Start();
    appointment.SetExamination("Жалобы", "Анамнез", "Осмотр");
    appointment.SetDiagnosis("R69");
    Check(patient.GetMedicalHistory().Count == 0, "Черновик попал в историю.");
    appointment.Complete();
    Expect<InvalidOperationException>(() => appointment.SetExamination("Изменение", "", "Изменение"));
    Expect<InvalidOperationException>(() => appointment.SetDiagnosis("R51"));
    Expect<InvalidOperationException>(() => appointment.AddPrescription("Изменение"));
    Expect<InvalidOperationException>(() => appointment.Complete());
    Check(patient.GetMedicalHistory().Single() == appointment, "Нет завершенного приема.");
    Check(!appointment.Slot.IsFree, "Завершение освободило использованный слот.");
});

Run("Два приема сохраняются раздельно, кабинет может измениться", () =>
{
    var clock = new DateTime(2030, 1, 10, 8, 0, 0);
    var clinic = new Clinic(() => clock);
    var doctor = clinic.AddDoctor("Врач", "Терапия", 20);
    var patient = clinic.AddPatient("Пациент", new DateOnly(1990, 1, 1), "0000000000000001");
    for (var i = 1; i <= 2; i++)
    {
        var start = clock.Date.AddDays(1).AddHours(9);
        var slot = clinic.CreateSchedule(doctor.Id, $"10{i}", start, start.AddMinutes(20))[0];
        var appointment = clinic.BookAppointment(patient.Id, slot.Id);
        clock = start;
        appointment.Start();
        appointment.SetExamination($"Жалобы {i}", "", $"Осмотр {i}");
        appointment.SetDiagnosis(i == 1 ? "R69" : "R51");
        clock = clock.AddMinutes(10);
        appointment.Complete();
    }
    var history = patient.GetMedicalHistory();
    Check(history.Count == 2 && history[0].Complaints == "Жалобы 1" && history[1].Complaints == "Жалобы 2", "История перезаписана.");
    Check(history[0].CabinetNumber == "101" && history[1].CabinetNumber == "102", "Кабинеты потеряны.");
});

Run("Одновременные заявки на один слот дают одну запись", () =>
{
    var (clinic, _, patient, anotherPatient, _) = CreateScenario();
    var results = new ConcurrentBag<Appointment>();
    Parallel.ForEach(new[] { patient, anotherPatient }, p =>
    {
        try { results.Add(clinic.BookAppointment(p.Id, 1)); }
        catch (InvalidOperationException) { }
    });
    Check(results.Count == 1 && patient.Appointments.Count + anotherPatient.Appointments.Count == 1, "Слот записан дважды.");
});

Run("Одновременные заявки пациента на одну специальность дают одну запись", () =>
{
    var (clinic, _, patient, _, _) = CreateScenario();
    var results = new ConcurrentBag<Appointment>();
    Parallel.ForEach(new[] { 1, 2 }, slotId =>
    {
        try { results.Add(clinic.BookAppointment(patient.Id, slotId)); }
        catch (InvalidOperationException) { }
    });
    Check(results.Count == 1 && patient.Appointments.Count == 1, "Нарушено ограничение по специальности.");
});

Console.WriteLine($"\nПройдено: {passed}. Ошибок: {failed}.");
return failed == 0 ? 0 : 1;

void Run(string name, Action check)
{
    try
    {
        check();
        passed++;
        Console.WriteLine($"OK: {name}");
    }
    catch (Exception error)
    {
        failed++;
        Console.WriteLine($"ОШИБКА: {name}: {error.Message}");
    }
}

static void Check(bool condition, string message)
{
    if (!condition)
        throw new Exception(message);
}

static void Expect<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception($"Ожидалось исключение {typeof(T).Name}.");
}

static (Clinic Clinic, Doctor Doctor, Patient Patient, Patient AnotherPatient, DateTime Day) CreateScenario()
{
    var time = new DateTime(2030, 1, 10, 8, 0, 0);
    var clinic = new Clinic(() => time);
    var doctor = clinic.AddDoctor("Врач", "Терапия", 20);
    var patient = clinic.AddPatient("Первый пациент", new DateOnly(1990, 1, 1), "0000000000000001");
    var anotherPatient = clinic.AddPatient("Второй пациент", new DateOnly(1991, 1, 1), "0000000000000002");
    var day = time.AddDays(1).Date.AddHours(9);
    clinic.CreateSchedule(doctor.Id, "101", day, day.AddHours(1));
    return (clinic, doctor, patient, anotherPatient, day);
}
