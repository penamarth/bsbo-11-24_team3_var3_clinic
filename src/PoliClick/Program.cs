using System.Text;
using PoliClick;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length > 0 && (args.Length != 1 || args[0] != "--demo"))
{
    Console.WriteLine("Запуск демонстрации: dotnet run --project src/PoliClick -- --demo");
    return 1;
}

Console.WriteLine("ИС «ПолиКлик»: запись и прием пациента");
Console.WriteLine("Учебные данные, время в сценарии меняется программно.\n");

var currentTime = DateTime.Today.AddHours(8);
var clinic = new Clinic(() => currentTime);
var doctor = clinic.AddDoctor("Иванова Анна Сергеевна", "Терапия", 20);
var anotherDoctor = clinic.AddDoctor("Смирнов Олег Петрович", "Терапия", 20);
clinic.AddDoctor("Зайцева Елена Игоревна", "Хирургия", 20);
var patient = clinic.AddPatient("Петров Алексей Игоревич", new DateOnly(1998, 4, 12), "0000000000000001");
var anotherPatient = clinic.AddPatient("Сидоров Илья Олегович", new DateOnly(1995, 7, 3), "0000000000000002");

var firstDay = currentTime.Date.AddDays(1).AddHours(9);
clinic.CreateSchedule(doctor.Id, "101", firstDay, firstDay.AddHours(1));
clinic.CreateSchedule(doctor.Id, "203", firstDay.AddDays(1), firstDay.AddDays(1).AddHours(1));
clinic.CreateSchedule(anotherDoctor.Id, "102", firstDay.AddDays(2), firstDay.AddDays(2).AddHours(1));

Console.WriteLine("1. Специальности:");
var specialties = clinic.GetSpecialties();
foreach (var item in specialties)
    Console.WriteLine($"   {item}");
var specialty = specialties[0];
Console.WriteLine($"   Выбрана специальность: {specialty}.");

Console.WriteLine("\n2. Поиск по дате: дни со свободным временем всех врачей специальности:");
var freeDates = clinic.GetFreeDates(specialty);
foreach (var date in freeDates)
    Console.WriteLine($"   {date:dd.MM.yyyy}");
var selectedDate = freeDates[0];
Console.WriteLine($"   Выбран день: {selectedDate:dd.MM.yyyy}. Свободное время:");
var freeSlots = clinic.FindFreeSlots(specialty, selectedDate);
foreach (var slot in freeSlots)
    Console.WriteLine($"   Слот {slot.Id}: {slot.Start:dd.MM.yyyy HH:mm}–{slot.End:HH:mm}, кабинет {slot.CabinetNumber}");

Console.WriteLine("\n3. Альтернативный поиск по ФИО: врачи выбранной специальности:");
var doctors = clinic.GetDoctors(specialty);
foreach (var item in doctors)
    Console.WriteLine($"   {item.Id}: {item.FullName}");
var selectedDoctor = doctors[0];
var doctorDates = clinic.GetFreeDates(specialty, selectedDoctor.Id);
Console.WriteLine($"   Выбран врач: {selectedDoctor.FullName}. Доступные дни:");
foreach (var date in doctorDates)
    Console.WriteLine($"   {date:dd.MM.yyyy}");
Console.WriteLine($"   Время выбранного врача на {doctorDates[0]:dd.MM.yyyy}:");
foreach (var slot in clinic.FindFreeSlots(specialty, doctorDates[0], selectedDoctor.Id))
    Console.WriteLine($"   {slot.Start:HH:mm}–{slot.End:HH:mm}, кабинет {slot.CabinetNumber}");

var firstSlot = freeSlots[0];
Console.WriteLine($"\n4. Выбран слот {firstSlot.Id}. Подтверждаем запись.");
var booking = clinic.BookAppointment(patient.Id, firstSlot.Id);
Console.WriteLine($"   Запись {booking.Number}: {booking.Patient.FullName}, {booking.Doctor.FullName}, кабинет {booking.CabinetNumber}.");

try
{
    clinic.BookAppointment(anotherPatient.Id, firstSlot.Id);
    throw new Exception("Ошибка демонстрации: удалось повторно занять слот.");
}
catch (InvalidOperationException error)
{
    Console.WriteLine($"   Повторная запись отклонена: {error.Message}");
}

// Врач один раз получает объект приема, затем работает с ним локально.
currentTime = firstSlot.Start;
var appointment = clinic.StartAppointment(doctor.Id, booking.Id);
appointment.SetExamination("Учебный пример жалоб", "Учебный анамнез", "Учебное описание осмотра");
appointment.SetDiagnosis("R69");
appointment.AddPrescription("Учебное назначение для демонстрации хранения данных.");
currentTime = currentTime.AddMinutes(17);
appointment.Complete();
Console.WriteLine($"\n5. Прием {appointment.Number} завершен: {appointment.StartedAt:HH:mm}–{appointment.FinishedAt:HH:mm}.");
Console.WriteLine("   Visit и MedicalRecord не создавались: результат находится в Appointment.");

try
{
    appointment.SetDiagnosis("R51");
    throw new Exception("Ошибка демонстрации: завершенный прием удалось изменить.");
}
catch (InvalidOperationException error)
{
    Console.WriteLine($"   Изменение после завершения отклонено: {error.Message}");
}

var nextDate = clinic.GetFreeDates(specialty, selectedDoctor.Id).First(date => date > selectedDate);
var secondSlot = clinic.FindFreeSlots(specialty, nextDate, selectedDoctor.Id)[0];
var secondAppointment = clinic.BookAppointment(patient.Id, secondSlot.Id);
currentTime = secondSlot.Start;
clinic.StartAppointment(doctor.Id, secondAppointment.Id);
secondAppointment.SetExamination("Жалобы второго приема", "Анамнез второго приема", "Осмотр второго приема");
secondAppointment.SetDiagnosis("R51");
currentTime = currentTime.AddMinutes(15);
secondAppointment.Complete();

Console.WriteLine("\n6. История пациента после двух приемов:");
foreach (var item in patient.GetMedicalHistory())
{
    Console.WriteLine($"   {item.Number}, {item.StartedAt:dd.MM.yyyy HH:mm}, кабинет {item.CabinetNumber}, код {item.DiagnosisCode}");
    Console.WriteLine($"   Жалобы: {item.Complaints}");
}

Console.WriteLine("\nИстория первого приема сохранена. Данные находятся в памяти до завершения программы.");
return 0;
