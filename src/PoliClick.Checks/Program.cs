using System.Collections.Concurrent;
using System.Text;
using PoliClick;

Console.OutputEncoding = Encoding.UTF8;
var failed = 0;
var passed = 0;

Run("Специальности не повторяются, врачи отбираются по специальности и сортируются по ФИО", () =>
{
    var (clinic, doctor, _, _, _) = CreateScenario();
    var anotherDoctor = clinic.AddDoctor("Абрамов Алексей", "терапия", 20);
    clinic.AddDoctor("Хирург", "Хирургия", 30);
    Check(clinic.GetSpecialties().SequenceEqual(new[] { "Терапия", "Хирургия" }), "Специальности повторяются или не отсортированы.");
    var doctors = clinic.GetDoctors(" терапия ");
    Check(doctors.Count == 2 && doctors[0] == anotherDoctor && doctors[1] == doctor,
        "Не учтены специальность, пробелы, регистр или порядок ФИО.");
});

Run("Дни свободного приема не повторяются и отсортированы", () =>
{
    var (clinic, doctor, _, _, day) = CreateScenario();
    var anotherDoctor = clinic.AddDoctor("Другой врач", "Терапия", 20);
    clinic.CreateSchedule(doctor.Id, "101", day.AddDays(2), day.AddDays(2).AddMinutes(20));
    clinic.CreateSchedule(anotherDoctor.Id, "102", day, day.AddHours(1));
    clinic.CreateSchedule(doctor.Id, "101", day.AddDays(-1), day.AddDays(-1).AddMinutes(20));
    var dates = clinic.GetFreeDates(" терапия ");
    Check(dates.SequenceEqual(new[] { DateOnly.FromDateTime(day.AddDays(-1)), DateOnly.FromDateTime(day),
        DateOnly.FromDateTime(day.AddDays(2)) }), "Дни повторяются, не отсортированы или потеряны.");
});

Run("Поиск по ФИО ограничивает дни и время выбранным врачом", () =>
{
    var (clinic, doctor, _, _, day) = CreateScenario();
    var anotherDoctor = clinic.AddDoctor("Другой врач", "Терапия", 20);
    clinic.CreateSchedule(anotherDoctor.Id, "102", day.AddDays(1), day.AddDays(1).AddHours(1));
    var firstDate = DateOnly.FromDateTime(day);
    var secondDate = firstDate.AddDays(1);
    Check(clinic.GetFreeDates("Терапия").SequenceEqual(new[] { firstDate, secondDate }), "Поиск по дате потерял врачей.");
    Check(clinic.GetFreeDates("Терапия", doctor.Id).SequenceEqual(new[] { firstDate }), "Подмешаны дни другого врача.");
    Check(clinic.GetFreeDates("Терапия", anotherDoctor.Id).SequenceEqual(new[] { secondDate }), "Неверные дни выбранного врача.");
    Check(clinic.FindFreeSlots("Терапия", firstDate, anotherDoctor.Id).Count == 0, "Подмешано чужое время.");
    var slots = clinic.FindFreeSlots("Терапия", secondDate, anotherDoctor.Id);
    Check(slots.Count == 3 && slots.All(s => s.Doctor == anotherDoctor), "Потерян фильтр врача.");
    Check(clinic.GetFreeDates("Хирургия", anotherDoctor.Id).Count == 0, "Потерян фильтр специальности.");
});

Run("Полностью занятый день исчезает из выбора и возвращается после отмены", () =>
{
    var (clinic, _, patient, anotherPatient, day) = CreateScenario();
    var third = clinic.AddPatient("Третий пациент", new DateOnly(1990, 1, 1), "0000000000000003");
    var appointment = clinic.BookAppointment(patient.Id, 1);
    clinic.BookAppointment(anotherPatient.Id, 2);
    clinic.BookAppointment(third.Id, 3);
    Check(clinic.GetFreeDates("Терапия").Count == 0, "Занятый день остался доступен.");
    clinic.CancelAppointment(patient.Id, appointment.Id);
    Check(clinic.GetFreeDates("Терапия").SequenceEqual(new[] { DateOnly.FromDateTime(day) }), "Освободившийся день не появился.");
});

Run("Доступные дни учитывают текущее время и включают весь последний день горизонта", () =>
{
    var clock = new DateTime(2030, 1, 10, 8, 0, 0);
    var (clinic, doctor, _, _, _) = CreateScenario(() => clock);
    var todaySlot = clock.Date.AddHours(9);
    var last = clock.Date.AddDays(14).AddHours(23);
    clinic.CreateSchedule(doctor.Id, "101", todaySlot, todaySlot.AddMinutes(20));
    clinic.CreateSchedule(doctor.Id, "101", last, last.AddMinutes(20));
    clinic.CreateSchedule(doctor.Id, "101", last.AddDays(1), last.AddDays(1).AddMinutes(20));
    clock = todaySlot;
    var dates = clinic.GetFreeDates("Терапия");
    Check(!dates.Contains(DateOnly.FromDateTime(todaySlot)), "Слот с наступившим началом попал в выбор дней.");
    Check(dates.Contains(DateOnly.FromDateTime(last)), "Последний день недоступен.");
    Check(!dates.Contains(DateOnly.FromDateTime(last.AddDays(1))), "Горизонт записи превышен.");
    Check(dates.All(date => clinic.FindFreeSlots("Терапия", date).Count > 0), "Доступный день не содержит свободного времени.");
});

Run("Пустые результаты и некорректная специальность обрабатываются при начальном выборе", () =>
{
    Check(new Clinic().GetSpecialties().Count == 0, "В пустой поликлинике появились специальности.");
    var (clinic, _, _, _, _) = CreateScenario();
    Check(clinic.GetDoctors("Нет такой специальности").Count == 0, "Найдены посторонние врачи.");
    Check(clinic.GetFreeDates("Нет такой специальности").Count == 0, "Найдены посторонние дни.");
    Check(clinic.GetFreeDates("Терапия", int.MaxValue).Count == 0, "Найдены дни неизвестного врача.");
    Expect<ArgumentException>(() => clinic.GetDoctors(" "));
    Expect<ArgumentException>(() => clinic.GetFreeDates(" "));
});

Run("Хранилища сохраняют объекты и не добавляют пациента с повторным полисом", () =>
{
    var (clinic, doctor, patient, anotherPatient, _) = CreateScenario();
    Check(ReferenceEquals(clinic.Patients[0], patient) && ReferenceEquals(clinic.Doctors[0], doctor),
        "Хранилища вернули копии объектов.");
    Expect<InvalidOperationException>(() => clinic.AddPatient("Дубликат", patient.BirthDate, patient.PolicyNumber));
    var third = clinic.AddPatient("Третий пациент", new DateOnly(1992, 1, 1), "0000000000000003");
    Check(clinic.Patients.Count == 3 && third.Id != patient.Id && third.Id != anotherPatient.Id,
        "Дубликат изменил коллекцию или идентификаторы повторились.");
    var appointment = clinic.BookAppointment(third.Id, 1);
    Check(ReferenceEquals(appointment.Patient, third), "Запись использует другого пациента.");
});

Run("Поиск отсутствующего пациента и врача не меняет данные", () =>
{
    var (clinic, _, patient, anotherPatient, day) = CreateScenario();
    Expect<KeyNotFoundException>(() => clinic.BookAppointment(int.MaxValue, 1));
    Expect<KeyNotFoundException>(() => clinic.CancelAppointment(int.MaxValue, 1));
    Expect<KeyNotFoundException>(() => clinic.CreateSchedule(int.MaxValue, "102", day, day.AddHours(1)));
    Check(clinic.Schedule.Slots.Count == 3 && clinic.Schedule.Slots.All(s => s.IsFree) &&
        patient.Appointments.Count == 0 && anotherPatient.Appointments.Count == 0,
        "Неуспешный поиск изменил расписание или записи.");
});

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
    Expect<InvalidOperationException>(() => clinic.StartAppointment(doctor.Id, appointment.Id));
    Expect<KeyNotFoundException>(() => clinic.CancelAppointment(anotherPatient.Id, appointment.Id));
    Check(appointment.Status == AppointmentStatus.PLANNED, "Запись изменилась.");
});

Run("Прием нельзя начать в другой день, неуспешный старт не меняет запись", () =>
{
    var clock = new DateTime(2030, 1, 10, 8, 0, 0);
    var (clinic, doctor, patient, _, day) = CreateScenario(() => clock);
    var appointment = clinic.BookAppointment(patient.Id, 1);
    Expect<InvalidOperationException>(() => clinic.StartAppointment(doctor.Id, appointment.Id));
    clock = day.AddDays(1);
    Expect<InvalidOperationException>(() => clinic.StartAppointment(doctor.Id, appointment.Id));
    Check(appointment.Status == AppointmentStatus.PLANNED && appointment.StartedAt is null,
        "После отказа изменились статус или время начала.");
});

Run("В назначенный день прием можно начать раньше слота и получить тот же объект", () =>
{
    var clock = new DateTime(2030, 1, 10, 8, 0, 0);
    var (clinic, doctor, patient, _, day) = CreateScenario(() => clock);
    var appointment = clinic.BookAppointment(patient.Id, 1);
    clock = day.AddMinutes(-10);
    var started = clinic.StartAppointment(doctor.Id, appointment.Id);
    Check(ReferenceEquals(started, appointment), "Создана копия приема.");
    Check(started.Status == AppointmentStatus.IN_PROGRESS && started.StartedAt == clock,
        "Не сохранено фактическое время начала.");
});

Run("Врач не может начать следующий прием, пока не завершит предыдущий", () =>
{
    var clock = new DateTime(2030, 1, 10, 8, 0, 0);
    var (clinic, doctor, patient, anotherPatient, day) = CreateScenario(() => clock);
    var first = clinic.BookAppointment(patient.Id, 1);
    var second = clinic.BookAppointment(anotherPatient.Id, 2);
    clock = day;
    clinic.StartAppointment(doctor.Id, first.Id);
    clock = day.AddMinutes(30);
    Expect<InvalidOperationException>(() => clinic.StartAppointment(doctor.Id, second.Id));
    Check(second.Status == AppointmentStatus.PLANNED && second.StartedAt is null, "Отказ изменил второй прием.");
    first.SetExamination("Жалобы", "", "Осмотр");
    first.SetDiagnosis("R69");
    first.Complete();
    clinic.StartAppointment(doctor.Id, second.Id);
    Check(second.StartedAt == clock, "Следующий прием не начался после завершения предыдущего.");
});

Run("Пациент не может участвовать в двух приемах разных врачей одновременно", () =>
{
    var clock = new DateTime(2030, 1, 10, 8, 0, 0);
    var (clinic, doctor, patient, _, day) = CreateScenario(() => clock);
    var anotherDoctor = clinic.AddDoctor("Хирург", "Хирургия", 20);
    var anotherSlot = clinic.CreateSchedule(anotherDoctor.Id, "102", day, day.AddMinutes(20))[0];
    var first = clinic.BookAppointment(patient.Id, 1);
    var second = clinic.BookAppointment(patient.Id, anotherSlot.Id);
    clock = day;
    clinic.StartAppointment(doctor.Id, first.Id);
    Expect<InvalidOperationException>(() => clinic.StartAppointment(anotherDoctor.Id, second.Id));
    Check(second.Status == AppointmentStatus.PLANNED && second.StartedAt is null, "Отказ изменил второй прием.");
});

Run("Затянувшийся прием блокирует кабинет для следующего врача", () =>
{
    var clock = new DateTime(2030, 1, 10, 8, 0, 0);
    var (clinic, doctor, patient, anotherPatient, day) = CreateScenario(() => clock);
    var anotherDoctor = clinic.AddDoctor("Хирург", "Хирургия", 20);
    var anotherSlot = clinic.CreateSchedule(anotherDoctor.Id, "101", day.AddHours(1), day.AddMinutes(80))[0];
    var first = clinic.BookAppointment(patient.Id, 1);
    var second = clinic.BookAppointment(anotherPatient.Id, anotherSlot.Id);
    clock = day;
    clinic.StartAppointment(doctor.Id, first.Id);
    clock = day.AddHours(1);
    Expect<InvalidOperationException>(() => clinic.StartAppointment(anotherDoctor.Id, second.Id));
    Check(second.Status == AppointmentStatus.PLANNED && second.StartedAt is null, "Отказ изменил второй прием.");
    first.SetExamination("Жалобы", "", "Осмотр");
    first.SetDiagnosis("R69");
    first.Complete();
    clinic.StartAppointment(anotherDoctor.Id, second.Id);
    Check(second.Status == AppointmentStatus.IN_PROGRESS, "Кабинет остался занят после завершения приема.");
});

Run("Независимые приемы разных врачей в разных кабинетах могут идти одновременно", () =>
{
    var clock = new DateTime(2030, 1, 10, 8, 0, 0);
    var (clinic, doctor, patient, anotherPatient, day) = CreateScenario(() => clock);
    var anotherDoctor = clinic.AddDoctor("Хирург", "Хирургия", 20);
    var anotherSlot = clinic.CreateSchedule(anotherDoctor.Id, "102", day, day.AddMinutes(20))[0];
    var first = clinic.BookAppointment(patient.Id, 1);
    var second = clinic.BookAppointment(anotherPatient.Id, anotherSlot.Id);
    clock = day;
    clinic.StartAppointment(doctor.Id, first.Id);
    clinic.StartAppointment(anotherDoctor.Id, second.Id);
    Check(first.Status == AppointmentStatus.IN_PROGRESS && second.Status == AppointmentStatus.IN_PROGRESS,
        "Независимые приемы блокируют друг друга.");
});

Run("Отмененный, завершенный и отсутствующий прием нельзя начать", () =>
{
    var clock = new DateTime(2030, 1, 10, 8, 0, 0);
    var (clinic, doctor, patient, _, day) = CreateScenario(() => clock);
    var cancelled = clinic.BookAppointment(patient.Id, 1);
    clinic.CancelAppointment(patient.Id, cancelled.Id);
    var completed = clinic.BookAppointment(patient.Id, 2);
    clock = day;
    Expect<InvalidOperationException>(() => clinic.StartAppointment(doctor.Id, cancelled.Id));
    Expect<KeyNotFoundException>(() => clinic.StartAppointment(doctor.Id, int.MaxValue));
    clinic.StartAppointment(doctor.Id, completed.Id);
    completed.SetExamination("Жалобы", "", "Осмотр");
    completed.SetDiagnosis("R69");
    completed.Complete();
    var startedAt = completed.StartedAt;
    Expect<InvalidOperationException>(() => clinic.StartAppointment(doctor.Id, completed.Id));
    Check(cancelled.Status == AppointmentStatus.CANCELLED && completed.Status == AppointmentStatus.COMPLETED &&
        completed.StartedAt == startedAt, "Повторное начало изменило историю.");
});

Run("Параллельные попытки начать приемы одного врача запускают только один", () =>
{
    var clock = new DateTime(2030, 1, 10, 8, 0, 0);
    var (clinic, doctor, patient, anotherPatient, day) = CreateScenario(() => clock);
    var first = clinic.BookAppointment(patient.Id, 1);
    var second = clinic.BookAppointment(anotherPatient.Id, 2);
    clock = day;
    var started = new ConcurrentBag<Appointment>();
    Parallel.ForEach(new[] { first, second }, appointment =>
    {
        try { started.Add(clinic.StartAppointment(doctor.Id, appointment.Id)); }
        catch (InvalidOperationException) { }
    });
    Check(started.Count == 1 && new[] { first, second }.Count(a => a.Status == AppointmentStatus.IN_PROGRESS) == 1,
        "У врача начались два приема.");
});

Run("Прием требует начала, жалоб, осмотра и диагноза", () =>
{
    var clock = new DateTime(2030, 1, 10, 8, 0, 0);
    var (clinic, doctor, patient, _, day) = CreateScenario(() => clock);
    var appointment = clinic.BookAppointment(patient.Id, 1);
    Expect<InvalidOperationException>(() => appointment.SetDiagnosis("R69"));
    Expect<InvalidOperationException>(() => appointment.Complete());
    clock = day;
    clinic.StartAppointment(doctor.Id, appointment.Id);
    Expect<InvalidOperationException>(() => clinic.StartAppointment(doctor.Id, appointment.Id));
    Expect<InvalidOperationException>(() => appointment.Complete());
    appointment.SetExamination("Жалобы", "", "Осмотр");
    Expect<InvalidOperationException>(() => appointment.Complete());
    Expect<ArgumentException>(() => appointment.SetDiagnosis("нет кода"));
    appointment.SetDiagnosis("j06.9");
    Check(appointment.DiagnosisCode == "J06.9", "Не нормализован код.");
});

Run("Черновик не входит в историю, завершенный прием нельзя менять", () =>
{
    var clock = new DateTime(2030, 1, 10, 8, 0, 0);
    var (clinic, doctor, patient, _, day) = CreateScenario(() => clock);
    var appointment = clinic.BookAppointment(patient.Id, 1);
    clock = day;
    clinic.StartAppointment(doctor.Id, appointment.Id);
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
        clinic.StartAppointment(doctor.Id, appointment.Id);
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

static (Clinic Clinic, Doctor Doctor, Patient Patient, Patient AnotherPatient, DateTime Day) CreateScenario(Func<DateTime>? now = null)
{
    var time = new DateTime(2030, 1, 10, 8, 0, 0);
    var clinic = new Clinic(now ?? (() => time));
    var doctor = clinic.AddDoctor("Врач", "Терапия", 20);
    var patient = clinic.AddPatient("Первый пациент", new DateOnly(1990, 1, 1), "0000000000000001");
    var anotherPatient = clinic.AddPatient("Второй пациент", new DateOnly(1991, 1, 1), "0000000000000002");
    var day = time.AddDays(1).Date.AddHours(9);
    clinic.CreateSchedule(doctor.Id, "101", day, day.AddHours(1));
    return (clinic, doctor, patient, anotherPatient, day);
}
