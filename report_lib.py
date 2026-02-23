# Порт VBA‑макроса ELEVATE на Python с управлением Excel через COM.


from __future__ import annotations

import datetime
import os
import fnmatch
import math
from dataclasses import dataclass, field
from typing import Any, List, Sequence, Tuple

try:
    import win32com.client as win32  # pyright: ignore[reportMissingImports]
except Exception:
    win32 = None

try:
    import numpy as _np  # pyright: ignore[reportMissingImports]
except Exception:
    _np = None


# Константы Excel (значения как в VBA/COM).
class XL:
    xlShiftDown = -4121
    xlFormatFromRightOrBelow = 1
    xlFormatFromLeftOrAbove = 0
    xlContinuous = 1
    xlThin = 2
    xlLeft = -4131
    xlWhole = 1
    xlPart = 2
    xlValue = 2


# Кэш активного Excel.Application, чтобы не создавать объект многократно.
_EXCEL_APP = None


# Проверяет наличие pywin32 и возвращает модуль.
def _require_win32() -> Any:
    if win32 is None:
        raise RuntimeError("pywin32 is required to run this module (pip install pywin32).")
    return win32


# Возвращает Excel.Application (active или новый).
def get_excel_app(visible: bool | None = None, use_active: bool = True):
    global _EXCEL_APP
    w = _require_win32()
    if _EXCEL_APP is None:
        if use_active:
            try:
                _EXCEL_APP = w.GetActiveObject("Excel.Application")
            except Exception:
                _EXCEL_APP = w.Dispatch("Excel.Application")
        else:
            _EXCEL_APP = w.Dispatch("Excel.Application")
    if visible is not None:
        _EXCEL_APP.Visible = visible
    return _EXCEL_APP


# Создает 1‑базовый список (индекс 0 — заглушка).
def new_1based_list(size: int, fill: Any = 0) -> List[Any]:
    return [fill] * (size + 1)


# Создает 2D 1‑базовый список.
def new_1based_2d(rows: int, cols: int, fill: Any = None) -> List[List[Any]]:
    return [[fill] * (cols + 1) for _ in range(rows + 1)]


# Изменяет размер 1‑базового списка с сохранением.
def redim_preserve(lst: List[Any], new_size: int, fill: Any = 0) -> List[Any]:
    current = len(lst) - 1
    if new_size <= current:
        return lst[: new_size + 1]
    return lst + [fill] * (new_size - current)


# UBound для 1‑базового списка.
def ubound(lst: List[Any]) -> int:
    return len(lst) - 1


# LBound для 1‑базового списка.
def lbound(lst: List[Any]) -> int:
    return 1


# Преобразует значение к float (VBA CDbl).
def cdbl(value: Any) -> float:
    if isinstance(value, (int, float)):
        return float(value)
    s = str(value).strip()
    if value is None or s == "":
        return 0.0
    s = s.replace(",", ".")
    return float(s)


# Преобразует значение к строке (VBA CStr).
def cstr(value: Any) -> str:
    if value is None:
        return ""
    if isinstance(value, float):
        if value.is_integer():
            return str(int(value))
        s = f"{value}"
        if "." in s:
            s = s.replace(".", ",")
        return s
    return str(value)


# Аналог VBA Right().
def right(text: str, length: int) -> str:
    if length <= 0:
        return ""
    return text[-length:]


# Аналог VBA Left().
def left(text: str, length: int) -> str:
    if length <= 0:
        return ""
    return text[:length]


# Упрощенный VBA Like (через fnmatch).
def vb_like(text: Any, pattern: str) -> bool:
    if text is None:
        return False
    return fnmatch.fnmatchcase(str(text), pattern)


# Цвет RGB в формате Excel (packed int).
def rgb(r: int, g: int, b: int) -> int:
    return int(r) + (int(g) << 8) + (int(b) << 16)


# Простая замена MsgBox (печать в консоль).
def msgbox(text: str) -> None:
    print(text)


# Формат dd.mm.yyyy.
def format_date(dt: datetime.date | None = None) -> str:
    if dt is None:
        dt = datetime.date.today()
    return dt.strftime("%d.%m.%Y")


@dataclass
# Данные здания (структура из VBA).
class BuildingDataType:
    NoFloors: int = 0
    FloorName: List[float] = field(default_factory=list)
    FloorHeight: List[float] = field(default_factory=list)
    FloorLevel: List[float] = field(default_factory=list)
    FloorType: List[str] = field(default_factory=list)
    FloorFactor: List[float] = field(default_factory=list)
    NoPeople: List[float] = field(default_factory=list)
    TotalPeople: float = 0.0
    cTotalPeople: float = 0.0
    EntranceFloor: List[str] = field(default_factory=list)
    NoExitFloors: int = 0
    Bias: List[float] = field(default_factory=list)
    BuildingType: str = ""
    Absenteeism: float = 0.0


@dataclass
# Данные лифтов (структура из VBA).
class ElevatorDataType:
    NoElevators: int = 0
    Dispatcher: str = ""
    Spec: List[List[Any]] = field(default_factory=list)
    FloorsServed: List[List[str]] = field(default_factory=list)


@dataclass
# Данные пассажиропотока (структура из VBA).
class PassengerDataType:
    Incoming: float = 0.0
    Outgoing: float = 0.0
    Interfloor: float = 0.0


# Считывает batch_results и формирует AWT/ATTD.
def CopyDataMain(excel=None) -> Tuple[str, str, List[Any], List[Any]]:
    excel = excel or get_excel_app()
    sheet = excel.ActiveSheet

    file_name = cstr(sheet.Cells(2, 1).Value)
    folder = cstr(sheet.Cells(2, 2).Value)

    n_steps = int(sheet.UsedRange.Rows.Count)

    awt = new_1based_list(n_steps - 1, 0)
    attd = new_1based_list(n_steps - 1, 0)

    for i in range(2, n_steps + 1):
        awt[i - 1] = sheet.Cells(i, 7).Value or 0
        attd[i - 1] = sheet.Cells(i, 11).Value or 0

    excel.ActiveWorkbook.Close(SaveChanges=False)

    return file_name, folder, awt, attd


# Считывает данные проекта и заполняет структуры.
def CopyDataSub(
    file_name: str,
    folder: str,
    excel=None,
) -> Tuple[List[str], BuildingDataType, ElevatorDataType, PassengerDataType]:
    excel = excel or get_excel_app()

    file_name = file_name + ".csv"
    folder = folder + "\\"

    excel.Workbooks.Open(Filename=folder + file_name, Local=True)
    sheet = excel.ActiveSheet

    job_data_row = 0
    anal_data_row = 0
    building_data_row = 0
    elevator_data_row = 0
    passenger_data_row = 0

    for i in range(1, int(sheet.UsedRange.Rows.Count) + 1):
        val = sheet.Cells(i, 1).Value
        if val == "JOB DATA":
            job_data_row = i
        if val == "ANALYSIS DATA":
            anal_data_row = i
        if val == "BUILDING DATA":
            building_data_row = i
        if val == "ELEVATOR DATA":
            elevator_data_row = i
        if val == "PASSENGER DATA":
            passenger_data_row = i

    job_data = new_1based_list(7, "")
    for i in range(1, 8):
        job_data[i] = sheet.Cells(job_data_row + i, 4).Value

    building_data = BuildingDataType()

    building_data.BuildingType = sheet.Cells(elevator_data_row - 8, 6).Value
    building_data.Absenteeism = cdbl(sheet.Cells(elevator_data_row - 9, 6).Value)
    if building_data.BuildingType == "Office":
        building_type = "Офис"
    elif building_data.BuildingType == "Residential":
        building_type = "Жилье"
    elif building_data.BuildingType == "Hotel":
        building_type = "Отель"
    else:
        building_type = "БКТ"

    absenteeism = (100 - building_data.Absenteeism) / 100

    building_data.NoFloors = int(cdbl(sheet.Cells(elevator_data_row - 2, 6).Value))
    building_data.TotalPeople = 0
    building_data.cTotalPeople = 0
    building_data.NoExitFloors = 0

    building_data.FloorName = new_1based_list(building_data.NoFloors, 0.0)
    building_data.FloorHeight = new_1based_list(building_data.NoFloors, 0.0)
    building_data.FloorLevel = new_1based_list(building_data.NoFloors, 0.0)
    building_data.FloorType = new_1based_list(building_data.NoFloors, "")
    building_data.FloorFactor = new_1based_list(building_data.NoFloors, 0.0)
    building_data.NoPeople = new_1based_list(building_data.NoFloors, 0)
    building_data.EntranceFloor = new_1based_list(building_data.NoFloors, "")
    building_data.Bias = new_1based_list(building_data.NoFloors, 0)

    t_floor_name: List[str] = []

    for i in range(1, building_data.NoFloors + 1):
        s_value1 = cstr(sheet.Cells(building_data_row + i + 1, 1).Value)
        s_len = len(s_value1)
        if s_len > 6:
            s_value2 = right(s_value1, s_len - 6)
        else:
            s_value2 = ""
        s_value2 = s_value2.replace(".", ",")

        building_data.FloorName[i] = cdbl(s_value2)
        building_data.FloorHeight[i] = cdbl(
            sheet.Cells(building_data_row + i + 1, 3).Value
        )

        building_data.NoPeople[i] = cdbl(
            sheet.Cells(building_data_row + i + 1, 5).Value
        )
        building_data.EntranceFloor[i] = cstr(
            sheet.Cells(building_data_row + i + 1, 11).Value
        )

        if building_data.FloorName[i] < 0:
            building_data.FloorType[i] = "Парковка"
            building_data.FloorFactor[i] = 1.2
        elif building_data.FloorName[i] > 0 and (building_data.EntranceFloor[i] == "Yes"):
            building_data.FloorType[i] = "Лобби"
            building_data.FloorFactor[i] = 0
        else:
            building_data.FloorType[i] = building_type
            building_data.FloorFactor[i] = absenteeism
            building_data.NoExitFloors = building_data.NoExitFloors + 1

        if building_data.EntranceFloor[i] == "Yes":
            for j in range(1, building_data.NoFloors + 1):
                cell_value = sheet.Cells(passenger_data_row + 15 + j, 1).Value
                if cell_value == s_value1:
                    building_data.Bias[i] = cdbl(
                        sheet.Cells(passenger_data_row + 15 + j, 4).Value
                    )
                    break

                if vb_like(cell_value, "*&*"):
                    t_floor_name = str(cell_value).split(" & ")
                    if (t_floor_name[0] == s_value1) or (
                        len(t_floor_name) > 1 and t_floor_name[1] == s_value1
                    ):
                        building_data.Bias[i] = cdbl(
                            sheet.Cells(passenger_data_row + 15 + j, 4).Value
                        )
                        break

        building_data.TotalPeople = building_data.TotalPeople + building_data.NoPeople[i]
        building_data.cTotalPeople = (
            building_data.cTotalPeople
            + building_data.NoPeople[i] * building_data.FloorFactor[i]
        )

    lower_level = 0.0

    if building_data.FloorName[1] < 1:
        for i in range(1, int(abs(building_data.FloorName[1])) + 1):
            lower_level = lower_level - building_data.FloorHeight[i]

    building_data.FloorLevel[1] = lower_level

    for i in range(2, building_data.NoFloors + 1):
        building_data.FloorLevel[i] = (
            building_data.FloorLevel[i - 1] + building_data.FloorHeight[i - 1]
        )

    for i in range(1, building_data.NoFloors + 1):
        if building_data.FloorType[i] == "Парковка":
            building_data.NoPeople[i] = (
                building_data.TotalPeople * building_data.Bias[i]
            ) / 120

    elevator_data = ElevatorDataType()
    elevator_data.Dispatcher = sheet.Cells(anal_data_row + 3, 6).Value

    elevator_data.NoElevators = 0
    for i in range(1, int(sheet.UsedRange.Columns.Count) + 1):
        if sheet.Cells(elevator_data_row + 1, 3 + i).Value != "":
            elevator_data.NoElevators = elevator_data.NoElevators + 1

    elevator_data.Spec = new_1based_2d(elevator_data.NoElevators, 10, None)
    for i in range(1, elevator_data.NoElevators + 1):
        for j in range(1, 11):
            elevator_data.Spec[i][j] = sheet.Cells(
                elevator_data_row + 1 + j, 3 + i
            ).Value

    elevator_data.FloorsServed = new_1based_2d(
        elevator_data.NoElevators, building_data.NoFloors, ""
    )
    for i in range(1, elevator_data.NoElevators + 1):
        for j in range(1, building_data.NoFloors + 1):
            elevator_data.FloorsServed[i][j] = sheet.Cells(
                elevator_data_row + 14 + j, 3 + i
            ).Value

    passenger_data = PassengerDataType()
    incoming = cstr(sheet.Cells(passenger_data_row + 4, 4).Value).replace(".", ",")
    outgoing = cstr(sheet.Cells(passenger_data_row + 5, 4).Value).replace(".", ",")
    interfloor = cstr(sheet.Cells(passenger_data_row + 6, 4).Value).replace(".", ",")

    passenger_data.Incoming = cdbl(incoming)
    passenger_data.Outgoing = cdbl(outgoing)
    passenger_data.Interfloor = cdbl(interfloor)

    excel.ActiveWorkbook.Close(SaveChanges=False)

    return job_data, building_data, elevator_data, passenger_data

# Заполняет шаблон и сохраняет отчет (XLSX + PDF).
def PrintData(
    xmlFolder: str,
    AWT: List[Any],
    ATTD: List[Any],
    AIS: List[Any],
    ALW: List[Any],
    JobData: List[str],
    BuildingData: BuildingDataType,
    ElevatorData: ElevatorDataType,
    PassengerData: PassengerDataType,
    excel=None,
    output_folder: str | None = None,
    template_folder: str | None = None,
) -> None:
    excel = excel or get_excel_app()

    file_name = BuildingData.BuildingType
    file_name = file_name + ".xlsx"
    template_folder = (
        template_folder
        or "T:\\Крупные проекты и высотное строительство\\Спецификации\\ELEVATE\\Meteor\\"
    )
    if not template_folder.endswith("\\"):
        template_folder = template_folder + "\\"

    excel.Workbooks.Open(Filename=template_folder + file_name)

    title = excel.ActiveWorkbook.Sheets("Титул")
    title.Cells(24, 5).Value = JobData[1]
    title.Cells(26, 5).Value = JobData[3]
    title.Cells(28, 5).Value = JobData[2]

    title.Rows(30).Delete()
    title.Rows(30).Delete()

    title.Cells(30, 4).Value = "Исполнитель:"
    title.Cells(30, 5).Value = JobData[4]

    title.Cells(32, 4).Value = "Дата:"
    title.Cells(32, 5).Value = format_date()

    is_served = new_1based_list(BuildingData.NoFloors, False)
    served_floors = 0
    for i in range(1, BuildingData.NoFloors + 1):
        is_served[i] = False
        for j in range(1, ElevatorData.NoElevators + 1):
            if ElevatorData.FloorsServed[j][i] == "Yes":
                is_served[i] = True
                served_floors = served_floors + 1
                break

    building_sheet = excel.ActiveWorkbook.Sheets("Здание")
    for i in range(1, BuildingData.NoFloors + 1):
        building_sheet.Rows(4).Insert(
            Shift=XL.xlShiftDown, CopyOrigin=XL.xlFormatFromRightOrBelow
        )
        building_sheet.Cells(4, 2).Value = BuildingData.FloorName[i]
        if "," in cstr(building_sheet.Cells(4, 2).Value):
            building_sheet.Cells(4, 2).Value = "'" + cstr(
                building_sheet.Cells(4, 2).Value
            ).replace(",", ".")
        building_sheet.Cells(4, 3).Value = BuildingData.FloorHeight[i]
        building_sheet.Cells(4, 4).Value = BuildingData.FloorLevel[i]
        building_sheet.Cells(4, 5).Value = BuildingData.FloorType[i]
        building_sheet.Cells(4, 6).Value = BuildingData.NoPeople[i]
        if is_served[i]:
            if BuildingData.NoPeople[i] != 0:
                building_sheet.Cells(4, 7).Value = BuildingData.FloorFactor[i]
            else:
                building_sheet.Cells(4, 7).Value = 0
        else:
            building_sheet.Cells(4, 7).Value = 0
        building_sheet.Cells(4, 8).Value = (
            (building_sheet.Cells(4, 6).Value or 0)
            * (building_sheet.Cells(4, 7).Value or 0)
        )

    building_sheet.Cells(4, 3).Value = "-"

    building_sheet.Cells(5 + BuildingData.NoFloors, 2).Value = "Итог:"
    building_sheet.Cells(5 + BuildingData.NoFloors, 2).Font.Bold = True

    building_sheet.Cells(5 + BuildingData.NoFloors, 8).Value = BuildingData.cTotalPeople
    building_sheet.Cells(5 + BuildingData.NoFloors, 8).Font.Bold = True

    flow_sheet = excel.ActiveWorkbook.Sheets("Пассажиропоток")
    flow_sheet.Cells(3, 3).Value = (
        "Входящий пассажиропоток\r\n(" + cstr(PassengerData.Incoming) + "%)"
    )
    flow_sheet.Cells(3, 8).Value = (
        "Выходящий пассажиропоток\r\n(" + cstr(PassengerData.Outgoing) + "%)"
    )
    flow_sheet.Cells(3, 13).Value = (
        "Межэтажный пассажиропоток\r\n(" + cstr(PassengerData.Interfloor) + "%)"
    )

    for i in range(1, BuildingData.NoFloors + 1):
        flow_sheet.Rows(5).Insert(
            Shift=XL.xlShiftDown, CopyOrigin=XL.xlFormatFromRightOrBelow
        )

        flow_sheet.Cells(5, 2).Value = BuildingData.FloorName[i]
        if "," in cstr(flow_sheet.Cells(5, 2).Value):
            flow_sheet.Cells(5, 2).Value = "'" + cstr(
                flow_sheet.Cells(5, 2).Value
            ).replace(",", ".")

        if PassengerData.Incoming != 0:
            if (BuildingData.EntranceFloor[i] == "Yes") and (BuildingData.Bias[i] != 0):
                flow_sheet.Cells(5, 3).Value = BuildingData.Bias[i] / 100
                flow_sheet.Cells(5, 3).NumberFormat = "0%"
                flow_sheet.Cells(5, 4).Value = flow_sheet.Cells(2, 19).Value
            elif (
                (BuildingData.EntranceFloor[i] == "No")
                and (is_served[i] is True)
                and (BuildingData.NoPeople[i] != 0)
            ):
                flow_sheet.Cells(5, 6).Value = flow_sheet.Cells(2, 19).Value
                flow_sheet.Cells(5, 7).Value = (
                    BuildingData.NoPeople[i] / BuildingData.TotalPeople
                )
                flow_sheet.Cells(5, 7).NumberFormat = "0.0%"

        if PassengerData.Outgoing != 0:
            if (
                (BuildingData.EntranceFloor[i] == "No")
                and (is_served[i] is True)
                and (BuildingData.NoPeople[i] != 0)
            ):
                flow_sheet.Cells(5, 8).Value = (
                    BuildingData.NoPeople[i] / BuildingData.TotalPeople
                )
                flow_sheet.Cells(5, 8).NumberFormat = "0.0%"
                flow_sheet.Cells(5, 9).Value = flow_sheet.Cells(2, 19).Value
            elif (BuildingData.EntranceFloor[i] == "Yes") and (
                BuildingData.Bias[i] != 0
            ):
                flow_sheet.Cells(5, 11).Value = flow_sheet.Cells(2, 19).Value
                flow_sheet.Cells(5, 12).Value = BuildingData.Bias[i] / 100
                flow_sheet.Cells(5, 12).NumberFormat = "0%"

        if PassengerData.Interfloor != 0:
            if (
                (BuildingData.EntranceFloor[i] == "No")
                and (is_served[i] is True)
                and (BuildingData.NoPeople[i] != 0)
            ):
                flow_sheet.Cells(5, 13).Value = (
                    BuildingData.NoPeople[i] / BuildingData.TotalPeople
                )
                flow_sheet.Cells(5, 13).NumberFormat = "0.0%"
                flow_sheet.Cells(5, 14).Value = flow_sheet.Cells(2, 19).Value
                flow_sheet.Cells(5, 16).Value = flow_sheet.Cells(2, 19).Value
                flow_sheet.Cells(5, 17).Value = (
                    BuildingData.NoPeople[i] / BuildingData.TotalPeople
                )
                flow_sheet.Cells(5, 17).NumberFormat = "0.0%"

    if PassengerData.Incoming != 0:
        flow_sheet.Range(
            flow_sheet.Cells(5, 5),
            flow_sheet.Cells(5 + BuildingData.NoFloors - 1, 5),
        ).BorderAround(LineStyle=XL.xlContinuous, Weight=XL.xlThin, Color=rgb(10, 39, 81))

    if PassengerData.Outgoing != 0:
        flow_sheet.Range(
            flow_sheet.Cells(5, 10),
            flow_sheet.Cells(5 + BuildingData.NoFloors - 1, 10),
        ).BorderAround(LineStyle=XL.xlContinuous, Weight=XL.xlThin, Color=rgb(10, 39, 81))

    if PassengerData.Interfloor != 0:
        flow_sheet.Range(
            flow_sheet.Cells(5, 15),
            flow_sheet.Cells(5 + BuildingData.NoFloors - 1, 15),
        ).BorderAround(LineStyle=XL.xlContinuous, Weight=XL.xlThin, Color=rgb(10, 39, 81))
    if vb_like(ElevatorData.Dispatcher, "*ACA*") or vb_like(
        ElevatorData.Dispatcher, "*Double*"
    ):
        dispatcher = "На этаж назначения (DDS)"
    else:
        dispatcher = "Собирательная при движении вверх и вниз"

    group_sheet = excel.ActiveWorkbook.Sheets("Лифтовая группа")
    group_sheet.Rows(14).Insert(
        Shift=XL.xlShiftDown, CopyOrigin=XL.xlFormatFromLeftOrAbove
    )

    group_sheet.Cells(14, 2).Value = "Система управления"
    group_sheet.Cells(14, 2).HorizontalAlignment = XL.xlLeft
    group_sheet.Cells(14, 2).Font.Bold = True

    group_sheet.Cells(14, 3).Value = dispatcher
    group_sheet.Cells(14, 3).HorizontalAlignment = XL.xlLeft
    group_sheet.Cells(14, 3).Font.Bold = True

    for i in range(1, ElevatorData.NoElevators + 1):
        group_sheet.Cells(4, 2 + i).Value = i
        for j in range(1, 10):
            if j < 5:
                group_sheet.Cells(4 + j, 2 + i).Value = ElevatorData.Spec[i][j]
            else:
                group_sheet.Cells(4 + j, 2 + i).Value = ElevatorData.Spec[i][j + 1]

    for i in range(1, BuildingData.NoFloors + 1):
        group_sheet.Rows(17).Insert(
            Shift=XL.xlShiftDown, CopyOrigin=XL.xlFormatFromRightOrBelow
        )
        group_sheet.Cells(17, 2).Value = BuildingData.FloorName[i]
        if "," in cstr(group_sheet.Cells(17, 2).Value):
            group_sheet.Cells(17, 2).Value = "'" + cstr(
                group_sheet.Cells(17, 2).Value
            ).replace(",", ".")

        for j in range(1, ElevatorData.NoElevators + 1):
            if ElevatorData.FloorsServed[j][i] == "Yes":
                group_sheet.Cells(17, 2 + j).Value = group_sheet.Cells(2, 13).Value
            else:
                group_sheet.Cells(17, 2 + j).Value = group_sheet.Cells(2, 12).Value

    group_sheet.Rows(6).Insert(
        Shift=XL.xlShiftDown, CopyOrigin=XL.xlFormatFromLeftOrAbove
    )
    group_sheet.Cells(6, 2).Value = "Площадь кабины, м2"

    for i in range(1, ElevatorData.NoElevators + 1):
        group_sheet.Cells(6, 2 + i).Value = floor_area_xml(xmlFolder, i, excel)
        group_sheet.Cells(6, 2 + i).NumberFormat = "0.00"

    group_sheet.Rows(11).Insert(
        Shift=XL.xlShiftDown, CopyOrigin=XL.xlFormatFromLeftOrAbove
    )
    group_sheet.Cells(11, 2).Value = "Ширина дверей, мм"
    group_sheet.Rows(12).Insert(
        Shift=XL.xlShiftDown, CopyOrigin=XL.xlFormatFromLeftOrAbove
    )
    group_sheet.Cells(12, 2).Value = "Тип дверей (ЦО/ТО)*"

    for i in range(1, ElevatorData.NoElevators + 1):
        d_open = group_sheet.Cells(14, 2 + i).Value
        d_close = group_sheet.Cells(15, 2 + i).Value
        d_width, d_type = door_type(d_open, d_close, "-", "-")
        group_sheet.Cells(11, 2 + i).Value = d_width
        group_sheet.Cells(12, 2 + i).Value = d_type

    total_rows = int(group_sheet.UsedRange.Rows.Count)
    group_sheet.Rows(total_rows + 1).Insert(
        Shift=XL.xlShiftDown, CopyOrigin=XL.xlFormatFromLeftOrAbove
    )
    group_sheet.Cells(total_rows + 2, 2).Value = (
        "*ЦО - центральное открывание, ТО - телескопическое открывание"
    )

    if BuildingData.BuildingType == "Office":
        target_cell = excel.ActiveWorkbook.Sheets("Оценка").UsedRange.Find(
            "Target WT", LookAt=XL.xlWhole
        )
        if target_cell is not None:
            if PassengerData.Incoming == 100:
                target_cell.Offset(0, 1).Value = 30
            elif PassengerData.Incoming == 85:
                target_cell.Offset(0, 1).Value = 35
            elif PassengerData.Incoming in (45, 40):
                target_cell.Offset(0, 1).Value = 40

    if BuildingData.BuildingType == "Residential":
        g_limit = 120
        n_steps = 8
    elif BuildingData.BuildingType == "Hotel":
        g_limit = 80
        n_steps = 13
    elif BuildingData.BuildingType == "Office":
        g_limit = 80
        n_steps = 13
    else:
        g_limit = 80
        n_steps = 13

    n_steps_from_main = ubound(AWT)

    AWT[:] = redim_preserve(AWT, n_steps)
    ATTD[:] = redim_preserve(ATTD, n_steps)
    AIS[:] = redim_preserve(AIS, n_steps)
    ALW[:] = redim_preserve(ALW, n_steps)

    for i in range(n_steps_from_main + 1, n_steps + 1):
        AWT[i] = i * g_limit
        ATTD[i] = i * g_limit
        AIS[i] = i * g_limit
        ALW[i] = i * g_limit

    QuickSort(AWT, lbound(AWT), ubound(AWT))
    QuickSort(ATTD, lbound(ATTD), ubound(ATTD))
    QuickSort(AIS, lbound(AIS), ubound(AIS))
    QuickSort(ALW, lbound(ALW), ubound(ALW))

    last_hc5 = n_steps
    for i in range(1, n_steps + 1):
        if AWT[i] > g_limit:
            last_hc5 = i - 1
            break

    linest(AIS, last_hc5)
    linest(ALW, last_hc5)

    QuickSort(AIS, lbound(AIS), ubound(AIS))
    QuickSort(ALW, lbound(ALW), ubound(ALW))

    rating_sheet = excel.ActiveWorkbook.Sheets("Оценка")
    r_col = rating_sheet.UsedRange.Find("Record", LookAt=XL.xlWhole).Column

    if BuildingData.BuildingType == "Residential":
        x_scale = 6
    else:
        x_scale = 8

    i_after = None
    for i in range(1, ubound(AWT) + 1):
        if AWT[i] < g_limit:
            rating_sheet.Cells(7, r_col + i).Value = AWT[i]
            rating_sheet.Cells(9, r_col + i).Value = ATTD[i]
            rating_sheet.Cells(11, r_col + i).Value = AIS[i]
            rating_sheet.Cells(13, r_col + i).Value = ALW[i]
        else:
            i_after = i
            break

    if i_after is None:
        i_after = ubound(AWT) + 1

    last_idx = max(1, i_after - 1)
    m_scale_ais = (math.floor(AIS[last_idx] / x_scale) + 2) * x_scale
    m_unit_ais = m_scale_ais / x_scale
    m_scale_alw = (math.floor(ALW[last_idx] / x_scale) + 2) * x_scale
    m_unit_alw = m_scale_alw / x_scale

    rating_sheet.ChartObjects("IS").Chart.Axes(XL.xlValue).MaximumScale = m_scale_ais
    rating_sheet.ChartObjects("IS").Chart.Axes(XL.xlValue).MajorUnit = m_unit_ais
    rating_sheet.ChartObjects("LW").Chart.Axes(XL.xlValue).MaximumScale = m_scale_alw
    rating_sheet.ChartObjects("LW").Chart.Axes(XL.xlValue).MajorUnit = m_unit_alw

    if i_after <= ubound(AWT):
        rating_sheet.Cells(8, r_col + i_after - 1).Value = AWT[i_after - 1]
        rating_sheet.Cells(10, r_col + i_after - 1).Value = ATTD[i_after - 1]
        rating_sheet.Cells(12, r_col + i_after - 1).Value = AIS[i_after - 1]
        rating_sheet.Cells(14, r_col + i_after - 1).Value = ALW[i_after - 1]

        for j in range(i_after, ubound(AWT) + 1):
            rating_sheet.Cells(8, r_col + j).Value = (
                (rating_sheet.Cells(6, r_col + j).Value or 0) * g_limit
            )
            rating_sheet.Cells(10, r_col + j).Value = (
                (rating_sheet.Cells(6, r_col + j).Value or 0) * g_limit
            )
            rating_sheet.Cells(12, r_col + j).Value = (
                (rating_sheet.Cells(6, r_col + j).Value or 0) * g_limit
            )
            rating_sheet.Cells(14, r_col + j).Value = (
                (rating_sheet.Cells(6, r_col + j).Value or 0) * g_limit
            )
    arr_start = [0]
    f_start = 0
    arr_finish = [0]
    f_finish = 0

    for i in range(2, BuildingData.NoFloors):
        if not is_served[i]:
            if is_served[i - 1] and is_served[i + 1]:
                f_start = f_start + 1
                arr_start = redim_preserve(arr_start, f_start)
                arr_start[f_start] = i

                f_finish = f_finish + 1
                arr_finish = redim_preserve(arr_finish, f_finish)
                arr_finish[f_finish] = i
            elif is_served[i - 1] and not is_served[i + 1]:
                f_start = f_start + 1
                arr_start = redim_preserve(arr_start, f_start)
                arr_start[f_start] = i
            elif (not is_served[i - 1]) and is_served[i + 1]:
                f_finish = f_finish + 1
                arr_finish = redim_preserve(arr_finish, f_finish)
                arr_finish[f_finish] = i

    s_floors = 2
    if f_start:
        for i in range(1, ubound(arr_start) + 1):
            if i > ubound(arr_finish) or arr_start[i] == 0 or arr_finish[i] == 0:
                break
            row_span = arr_finish[i] - arr_start[i]

            if row_span <= s_floors:
                for k in range(1, row_span + 2):
                    row_ref = building_sheet.Range("B1:B1000").Find(
                        BuildingData.FloorName[arr_start[i]], LookAt=XL.xlWhole
                    )
                    if row_ref is None:
                        continue
                    row_ref.Offset(1 - k, 3).Value = "Техэтаж"
            else:
                row_to_hide_ref = building_sheet.Range("B1:B1000").Find(
                    BuildingData.FloorName[arr_start[i]], LookAt=XL.xlWhole
                )
                if row_to_hide_ref is None:
                    continue
                row_to_hide = row_to_hide_ref.Row
                building_sheet.Cells(row_to_hide, 5).Value = "Экспресс зона"
                building_sheet.Cells(row_to_hide, 4).Value = "-"
                ex_height = 0.0
                for k in range(0, row_span + 1):
                    ex_height = ex_height + (
                        building_sheet.Cells(row_to_hide - k, 3).Value or 0
                    )
                building_sheet.Cells(row_to_hide, 3).Value = ex_height
                building_sheet.Cells(row_to_hide, 2).Value = (
                    "'"
                    + cstr(BuildingData.FloorName[arr_start[i]])
                    + " - "
                    + cstr(BuildingData.FloorName[arr_finish[i]])
                )
                building_sheet.Rows(
                    f"{row_to_hide - row_span}:{row_to_hide - 1}"
                ).EntireRow.Hidden = True

                row_to_hide_ref = flow_sheet.Range("B1:B1000").Find(
                    BuildingData.FloorName[arr_start[i]], LookAt=XL.xlWhole
                )
                if row_to_hide_ref is None:
                    continue
                row_to_hide = row_to_hide_ref.Row
                flow_sheet.Cells(row_to_hide, 2).Value = (
                    "'"
                    + cstr(BuildingData.FloorName[arr_start[i]])
                    + " - "
                    + cstr(BuildingData.FloorName[arr_finish[i]])
                )
                flow_sheet.Rows(
                    f"{row_to_hide - row_span}:{row_to_hide - 1}"
                ).EntireRow.Hidden = True

                row_to_hide_ref = group_sheet.Range("B1:B1000").Find(
                    BuildingData.FloorName[arr_start[i]], LookAt=XL.xlWhole
                )
                if row_to_hide_ref is None:
                    continue
                row_to_hide = row_to_hide_ref.Row
                group_sheet.Cells(row_to_hide, 2).Value = (
                    "'"
                    + cstr(BuildingData.FloorName[arr_start[i]])
                    + " - "
                    + cstr(BuildingData.FloorName[arr_finish[i]])
                )
                group_sheet.Rows(
                    f"{row_to_hide - row_span}:{row_to_hide - 1}"
                ).EntireRow.Hidden = True

    if vb_like(ElevatorData.Dispatcher, "*Double*"):
        row_lobby = 0
        no_lobby = 0
        for i in range(1, BuildingData.NoFloors + 1):
            if BuildingData.FloorType[i] == "Лобби":
                no_lobby = no_lobby + 1
                row_lobby_ref = building_sheet.Range("B1:B1000").Find(
                    BuildingData.FloorName[i], LookAt=XL.xlWhole
                )
                if row_lobby_ref is None:
                    continue
                row_lobby = row_lobby_ref.Row
                building_sheet.Cells(row_lobby, 5).Value = (
                    cstr(building_sheet.Cells(row_lobby, 5).Value)
                    + " #"
                    + cstr(no_lobby)
                )

    if vb_like(ElevatorData.Dispatcher, "*Double*"):
        if PassengerData.Incoming != 0:
            i = 2
            while i <= BuildingData.NoFloors:
                if (BuildingData.EntranceFloor[i] == "Yes") and (
                    BuildingData.EntranceFloor[i - 1] == "Yes"
                ):
                    row_to_merge_ref = flow_sheet.Range("B1:B1000").Find(
                        BuildingData.FloorName[i], LookAt=XL.xlWhole
                    )
                    if row_to_merge_ref is None:
                        i = i + 1
                        continue
                    row_to_merge = row_to_merge_ref.Row
                    flow_sheet.Range(
                        flow_sheet.Cells(row_to_merge, 3),
                        flow_sheet.Cells(row_to_merge + 1, 3),
                    ).Merge()
                    flow_sheet.Range(
                        flow_sheet.Cells(row_to_merge, 4),
                        flow_sheet.Cells(row_to_merge + 1, 4),
                    ).Merge()
                    i = i + 1
                i = i + 1

        if PassengerData.Outgoing != 0:
            i = 2
            while i <= BuildingData.NoFloors:
                if (BuildingData.EntranceFloor[i] == "Yes") and (
                    BuildingData.EntranceFloor[i - 1] == "Yes"
                ):
                    row_to_merge_ref = flow_sheet.Range("B1:B1000").Find(
                        BuildingData.FloorName[i], LookAt=XL.xlWhole
                    )
                    if row_to_merge_ref is None:
                        i = i + 1
                        continue
                    row_to_merge = row_to_merge_ref.Row
                    flow_sheet.Range(
                        flow_sheet.Cells(row_to_merge, 11),
                        flow_sheet.Cells(row_to_merge + 1, 11),
                    ).Merge()
                    flow_sheet.Range(
                        flow_sheet.Cells(row_to_merge, 12),
                        flow_sheet.Cells(row_to_merge + 1, 12),
                    ).Merge()
                    i = i + 1
                i = i + 1

        if PassengerData.Interfloor != 0:
            i = 2
            while i <= BuildingData.NoFloors:
                if (BuildingData.EntranceFloor[i] == "Yes") and (
                    BuildingData.EntranceFloor[i - 1] == "Yes"
                ):
                    row_to_merge_ref = flow_sheet.Range("B1:B1000").Find(
                        BuildingData.FloorName[i], LookAt=XL.xlWhole
                    )
                    if row_to_merge_ref is None:
                        i = i + 1
                        continue
                    row_to_merge = row_to_merge_ref.Row
                    flow_sheet.Range(
                        flow_sheet.Cells(row_to_merge, 16),
                        flow_sheet.Cells(row_to_merge + 1, 16),
                    ).Merge()
                    flow_sheet.Range(
                        flow_sheet.Cells(row_to_merge, 17),
                        flow_sheet.Cells(row_to_merge + 1, 17),
                    ).Merge()
                    i = i + 1
                i = i + 1

    if vb_like(ElevatorData.Dispatcher, "*Double*"):
        for i in range(1, ElevatorData.NoElevators + 1):
            group_sheet.Cells(5, i + 2).Value = "2x" + cstr(
                group_sheet.Cells(5, i + 2).Value
            )
            group_sheet.Cells(6, i + 2).Value = "2x" + cstr(
                group_sheet.Cells(6, i + 2).Value
            )

    eRate(AWT, ATTD, AIS, ALW, BuildingData, PassengerData, g_limit, excel)

    excel.ActiveWorkbook.Sheets("Оценка").Cells(4, 2).Value = eGroup(
        ElevatorData, served_floors, BuildingData.NoFloors
    )
    excel.ActiveWorkbook.Sheets("Критерии").Cells(10, 2).Value = eFlow(PassengerData)

    b_length = 0
    b_length = (
        29
        + len(cstr(PassengerData.Incoming))
        + len(cstr(PassengerData.Outgoing))
        + len(cstr(PassengerData.Interfloor))
    )

    excel.ActiveWorkbook.Sheets("Критерии").Cells(10, 2).Characters(
        Start=1, Length=b_length
    ).Font.FontStyle = "Bold"

    excel.ActiveWorkbook.Sheets("Оценка").Activate()

    base_name = cstr(JobData[1]) + " " + cstr(JobData[2])
    file_name = base_name + ".xlsx"
    output_folder = (
        output_folder or "T:\\Крупные проекты и высотное строительство\\_Ele_temp\\"
    )
    os.makedirs(output_folder, exist_ok=True)
    if not output_folder.endswith("\\"):
        output_folder = output_folder + "\\"

    excel.DisplayAlerts = False
    excel.ActiveWorkbook.SaveAs(Filename=output_folder + file_name)
    excel.ActiveWorkbook.ExportAsFixedFormat(
        Type=0, Filename=output_folder + base_name + ".pdf"
    )
    excel.DisplayAlerts = True


# Справочник площади кабины по грузоподъемности.
def floor_area(capacity: str) -> str:
    result = "-"
    ref = {
        "320": "'0,94",
        "450": "'1,20",
        "550": "'1,43",
        "630": "'1,54",
        "825": "'1,96",
        "1000": "'2,31",
        "1050": "'2,31",
        "1200": "'2,73",
        "1350": "'2,94",
        "1600": "'3,36",
        "1800": "'3,78",
        "2000": "'4,20",
        "2250": "'4,41",
        "2500": "'4,83",
    }
    if capacity in ref:
        result = ref[capacity]
    return result


# Читает площадь кабины из floor_area.csv.
def floor_area_xml(folder: str, i: int, excel=None) -> float:
    try:
        excel = excel or get_excel_app()
        file_name = "floor_area.csv"
        folder = folder + "\\"
        excel.Workbooks.Open(Filename=folder + file_name, Local=True)
        area = excel.ActiveSheet.Cells(i + 1, 2).Value
        result = cdbl(cstr(area).replace(".", ","))
        excel.ActiveWorkbook.Close(SaveChanges=False)
        return result
    except Exception:
        return 0.0


# Определяет тип/ширину дверей по временам.
def door_type(d_open: float, d_close: float, d_width: str, d_type: str) -> Tuple[str, str]:
    d_times = cstr(d_open) + "-" + cstr(d_close)
    ref = {
        "2,1-3,7": "700ТО",
        "2,2-3,9": "750ТО",
        "2,3-4,1": "800ТО",
        "2,4-4,3": "850ТО",
        "2,5-4,5": "900ТО",
        "2,6-4,7": "950ТО",
        "2,6-4,9": "1000ТО",
        "2,7-5,1": "1050ТО",
        "2,8-5,3": "1100ТО",
        "2,9-5,5": "1150ТО",
        "2,9-5,7": "1200ТО",
        "3-5,9": "1250ТО",
        "3,1-6": "1300ТО",
        "1,5-2,2": "600ЦО",
        "1,6-2,3": "650ЦО",
        "1,6-2,4": "700ЦО",
        "1,7-2,5": "750ЦО",
        "1,7-2,6": "800ЦО",
        "1,7-2,7": "850ЦО",
        "1,7-2,8": "900ЦО",
        "1,8-2,9": "1000ЦО",
        "1,9-3": "1050ЦО",
        "1,9-3,1": "1100ЦО",
        "2-3,2": "1150ЦО",
        "2-3,3": "1200ЦО",
        "2,1-3,4": "1250ЦО",
        "2,1-3,5": "1300ЦО",
    }

    if d_times in ref:
        d_width = "'" + left(ref[d_times], len(ref[d_times]) - 2)
        d_type = right(ref[d_times], 2)
    return d_width, d_type


# Парсит CSV шага и рассчитывает AIS/ALW.
def csvParse(
    file_name: str,
    folder: str,
    AIS: List[Any],
    ALW: List[Any],
    BuildingData: BuildingDataType,
    ElevatorData: ElevatorDataType,
    step: int,
    excel=None,
) -> None:
    if step < 10:
        file_name = left(file_name, len(file_name) - 3) + " 0" + cstr(step)
    else:
        file_name = left(file_name, len(file_name) - 3) + " " + cstr(step)

    file_name = file_name + ".csv"
    folder = folder + "\\"

    excel = excel or get_excel_app()
    excel.Workbooks.Open(Filename=folder + file_name, Local=True)
    sheet = excel.ActiveSheet

    s_row = sheet.UsedRange.Find("breakdown", LookAt=XL.xlPart).Row + 12
    e_row = sheet.UsedRange.Find("spatial", LookAt=XL.xlPart).Row - 2

    t_pass = 0
    lw = 0.0
    for i in range(s_row, e_row + 1):
        t_pass = t_pass + 1
        if (sheet.Cells(i, 11).Value or 0) >= 90:
            lw = lw + 1

    if t_pass:
        ALW[step] = 100 * lw / t_pass
    else:
        ALW[step] = 0

    c_row = sheet.UsedRange.Find("spatial", LookAt=XL.xlPart).Row + 2

    s_ais = new_1based_list(ElevatorData.NoElevators, 0.0)
    sum_ais = 0.0

    for i in range(1, ElevatorData.NoElevators + 1):
        s_value1 = cstr(ElevatorData.Spec[i][5])
        s_len = len(s_value1)
        if s_len > 6:
            s_value2 = right(s_value1, s_len - 6)
        else:
            s_value2 = ""
        s_value2 = s_value2.replace(".", ",")

        h_floor = cdbl(s_value2)

        h_floor_ind = 0
        for j in range(1, BuildingData.NoFloors + 1):
            if h_floor == BuildingData.FloorName[j]:
                h_floor_ind = j
                break

        nr_trip = 0
        sum_stop = 0
        nr_stop = new_1based_list(1, 0)

        while (sheet.Cells(c_row, 1).Value == i) and (
            sheet.Cells(c_row + 1, 1).Value == i
        ):
            if (
                (sheet.Cells(c_row, 3).Value or 0) == h_floor_ind
                and (sheet.Cells(c_row + 1, 3).Value or 0) == h_floor_ind
            ):
                nr_trip = nr_trip + 1
                nr_stop = redim_preserve(nr_stop, nr_trip, 0)
                nr_stop[nr_trip] = 0

                while ((sheet.Cells(c_row + 2, 3).Value or 0) != h_floor_ind) and (
                    sheet.Cells(c_row + 2, 1).Value == i
                ):
                    if (sheet.Cells(c_row + 2, 3).Value or 0) != (
                        sheet.Cells(c_row + 3, 3).Value or 0
                    ):
                        nr_stop[nr_trip] = nr_stop[nr_trip] + 1
                    c_row = c_row + 1
            c_row = c_row + 1
        c_row = c_row + 1

        for x in range(1, ubound(nr_stop) + 1):
            sum_stop = sum_stop + nr_stop[x]

        if nr_trip:
            s_ais[i] = sum_stop / nr_trip - 1
        else:
            s_ais[i] = 0

    for x in range(1, ubound(s_ais) + 1):
        sum_ais = sum_ais + s_ais[x]

    if ubound(s_ais):
        AIS[step] = sum_ais / ubound(s_ais)
    else:
        AIS[step] = 0

    excel.ActiveWorkbook.Close(SaveChanges=False)


# Сортировка массива как в VBA.
def QuickSort(arr: List[Any], low: int, high: int) -> None:
    if low < high:
        pivot = arr[(low + high) // 2]
        i = low
        j = high

        while i <= j:
            while arr[i] < pivot and i < high:
                i = i + 1
            while arr[j] > pivot and j > low:
                j = j - 1
            if i <= j:
                temp = arr[i]
                arr[i] = arr[j]
                arr[j] = temp
                i = i + 1
                j = j - 1

        if low < j:
            QuickSort(arr, low, j)
        if i < high:
            QuickSort(arr, i, high)


# Рассчитывает рейтинг обслуживания.
def eRate(
    AWT: List[Any],
    ATTD: List[Any],
    AIS: List[Any],
    ALW: List[Any],
    BuildingData: BuildingDataType,
    PassengerData: PassengerDataType,
    g_limit: int,
    excel=None,
) -> None:
    excel = excel or get_excel_app()
    sheet = excel.ActiveWorkbook.Sheets("Оценка")

    sheet.Cells(47, 2).Value = (
        cstr(PassengerData.Incoming)
        + "%, "
        + cstr(PassengerData.Outgoing)
        + "%, "
        + cstr(PassengerData.Interfloor)
        + "%"
    )

    if BuildingData.BuildingType == "Office":
        if sheet.Cells(47, 2).Value == excel.ActiveWorkbook.Sheets("Критерии").Cells(5, 4).Value:
            if (AWT[13] < 25) and (ATTD[13] < 80):
                assPrint(AWT, ATTD, AIS, ALW, 13, 5, BuildingData, excel)
            elif (AWT[12] < 30) and (ATTD[12] < 100):
                assPrint(AWT, ATTD, AIS, ALW, 12, 4, BuildingData, excel)
            elif (AWT[11] < 40) and (ATTD[11] < 120):
                assPrint(AWT, ATTD, AIS, ALW, 11, 3, BuildingData, excel)
            else:
                for i in range(13, 0, -1):
                    if AWT[i] < g_limit:
                        assPrint(AWT, ATTD, AIS, ALW, i, 1, BuildingData, excel)
                        break
        elif sheet.Cells(47, 2).Value == excel.ActiveWorkbook.Sheets("Критерии").Cells(6, 4).Value:
            if (AWT[12] < 25) and (ATTD[12] < 80):
                assPrint(AWT, ATTD, AIS, ALW, 12, 5, BuildingData, excel)
            elif (AWT[11] < 40) and (ATTD[11] < 100):
                assPrint(AWT, ATTD, AIS, ALW, 11, 4, BuildingData, excel)
            elif (AWT[10] < 40) and (ATTD[10] < 120):
                assPrint(AWT, ATTD, AIS, ALW, 10, 3, BuildingData, excel)
            else:
                for i in range(13, 0, -1):
                    if AWT[i] < g_limit:
                        assPrint(AWT, ATTD, AIS, ALW, i, 1, BuildingData, excel)
                        break
        else:
            msgbox("No Rating Data.")

    elif BuildingData.BuildingType == "Hotel":
        if sheet.Cells(47, 2).Value == excel.ActiveWorkbook.Sheets("Критерии").Cells(7, 4).Value:
            if (AWT[13] < 25) and (ATTD[13] < 80):
                assPrint(AWT, ATTD, AIS, ALW, 13, 5, BuildingData, excel)
            elif (AWT[12] < 40) and (ATTD[12] < 100):
                assPrint(AWT, ATTD, AIS, ALW, 12, 4, BuildingData, excel)
            elif (AWT[11] < 40) and (ATTD[11] < 120):
                assPrint(AWT, ATTD, AIS, ALW, 11, 3, BuildingData, excel)
            else:
                for i in range(13, 0, -1):
                    if AWT[i] < g_limit:
                        assPrint(AWT, ATTD, AIS, ALW, i, 1, BuildingData, excel)
                        break
        else:
            msgbox("No Rating Data.")

    elif BuildingData.BuildingType == "Residential":
        if sheet.Cells(47, 2).Value == excel.ActiveWorkbook.Sheets("Критерии").Cells(8, 4).Value:
            if (AWT[8] < 40) and (ATTD[8] < 90):
                assPrint(AWT, ATTD, AIS, ALW, 8, 5, BuildingData, excel)
            elif (AWT[7] < 60) and (ATTD[7] < 120):
                assPrint(AWT, ATTD, AIS, ALW, 7, 4, BuildingData, excel)
            elif (AWT[6] < 60) and (ATTD[6] < 150):
                assPrint(AWT, ATTD, AIS, ALW, 6, 3, BuildingData, excel)
            else:
                for i in range(8, 0, -1):
                    if AWT[i] < g_limit:
                        assPrint(AWT, ATTD, AIS, ALW, i, 1, BuildingData, excel)
                        break
        else:
            msgbox("No Rating Data.")
    else:
        msgbox("No Rating Data.")


# Записывает рейтинг в лист «Оценка».
def assPrint(
    AWT: List[Any],
    ATTD: List[Any],
    AIS: List[Any],
    ALW: List[Any],
    HC5: int,
    Rating: int,
    BuildingData: BuildingDataType,
    excel=None,
) -> None:
    excel = excel or get_excel_app()
    sheet = excel.ActiveWorkbook.Sheets("Оценка")

    if BuildingData.BuildingType == "Residential":
        sheet.Cells(47, 5).Value = HC5
        sheet.Cells(47, 9).Value = AWT[HC5]
        sheet.Cells(47, 13).Value = ATTD[HC5]
        sheet.Cells(47, 17).Value = AIS[HC5]
        sheet.Cells(47, 21).Value = ALW[HC5]
        sheet.Cells(47, 25).Value = sheet.Cells(45, 29 + Rating).Value
        if Rating == 1:
            sheet.Cells(47, 25).Font.Color = rgb(255, 0, 0)
    else:
        sheet.Cells(47, 4).Value = HC5
        sheet.Cells(47, 6).Value = AWT[HC5]
        sheet.Cells(47, 8).Value = ATTD[HC5]
        sheet.Cells(47, 10).Value = AIS[HC5]
        sheet.Cells(47, 12).Value = ALW[HC5]
        sheet.Cells(47, 14).Value = sheet.Cells(45, 16 + Rating).Value
        if Rating == 1:
            sheet.Cells(47, 14).Font.Color = rgb(255, 0, 0)


# Формирует строку описания лифтовой группы.
def eGroup(ElevatorData: ElevatorDataType, s_floors: int, floors: int) -> str:
    ecap: dict[Any, int] = {}
    for i in range(1, ElevatorData.NoElevators + 1):
        key = ElevatorData.Spec[i][1]
        if key not in ecap:
            ecap[key] = 1
        else:
            ecap[key] = ecap[key] + 1

    x2 = ""
    if vb_like(ElevatorData.Dispatcher, "*Double*"):
        x2 = "2x"

    n_ele = ""
    for cap in ecap:
        if ecap[cap] < 2:
            n_quo = "лифт"
        elif ecap[cap] < 5:
            n_quo = "лифта"
        else:
            n_quo = "лифтов"
        n_ele = (
            n_ele
            + " "
            + cstr(ecap[cap])
            + " "
            + n_quo
            + " с грузоподъемностью "
            + x2
            + cstr(cap)
            + " кг,"
        )

    return (
        "Лифтовая группа:"
        + n_ele
        + " со скоростью "
        + cstr(ElevatorData.Spec[1][2])
        + " м/с. Количество остановок "
        + cstr(s_floors)
        + "/"
        + cstr(floors)
        + "."
    )


# Формирует строку описания пассажиропотока.
def eFlow(PassengerData: PassengerDataType) -> str:
    return (
        "Тип пассажиропотока ("
        + cstr(PassengerData.Incoming)
        + "%, "
        + cstr(PassengerData.Outgoing)
        + "%, "
        + cstr(PassengerData.Interfloor)
        + "%): "
        + "направление движения пассажиров во время часа пик.\r\n"
        + "Входной пассажиропоток ("
        + cstr(PassengerData.Incoming)
        + "%) - "
        + "пассажиропоток с конкретного посадочного этажа при входе в здание.\r\n"
        + "Выходной пассажиропоток ("
        + cstr(PassengerData.Outgoing)
        + "%) - "
        + "пассажиропоток с этажей здания на выход из здания.\r\n"
        + "Межэтажный пассажиропоток ("
        + cstr(PassengerData.Interfloor)
        + "%) - "
        + "одновременное перемещение пассажиров между этажами задния."
    )


# Решает СЛАУ методом Гаусса.
def _solve_linear_system(a: List[List[float]], b: List[float]) -> List[float]:
    n = len(a)
    for i in range(n):
        max_row = max(range(i, n), key=lambda r: abs(a[r][i]))
        if abs(a[max_row][i]) < 1e-12:
            return [0.0] * n
        if max_row != i:
            a[i], a[max_row] = a[max_row], a[i]
            b[i], b[max_row] = b[max_row], b[i]
        pivot = a[i][i]
        for j in range(i, n):
            a[i][j] = a[i][j] / pivot
        b[i] = b[i] / pivot
        for r in range(n):
            if r == i:
                continue
            factor = a[r][i]
            if factor == 0:
                continue
            for c in range(i, n):
                a[r][c] = a[r][c] - factor * a[i][c]
            b[r] = b[r] - factor * b[i]
    return b


# Аппроксимация 4‑й степени без numpy.
def _polyfit_deg4(xs: Sequence[float], ys: Sequence[float]) -> List[float]:
    s = [0.0] * 9
    for x in xs:
        p = 1.0
        for k in range(9):
            s[k] += p
            p *= x

    t = [0.0] * 5
    for x, y in zip(xs, ys):
        p = 1.0
        for k in range(5):
            t[k] += y * p
            p *= x

    a = [
        [s[8], s[7], s[6], s[5], s[4]],
        [s[7], s[6], s[5], s[4], s[3]],
        [s[6], s[5], s[4], s[3], s[2]],
        [s[5], s[4], s[3], s[2], s[1]],
        [s[4], s[3], s[2], s[1], s[0]],
    ]
    b = [t[4], t[3], t[2], t[1], t[0]]
    return _solve_linear_system(a, b)


# Аппроксимация как Excel LINEST для данных массива.
def linest(data: List[Any], last_hc5: int) -> None:
    if last_hc5 < 1:
        return

    xs = list(range(1, last_hc5 + 1))
    ys = [float(data[i]) for i in xs]

    if _np is not None:
        coeffs = _np.polyfit(xs, ys, 4).tolist()
    else:
        coeffs = _polyfit_deg4(xs, ys)

    x4, x3, x2, x1, x0 = coeffs
    for i in range(1, last_hc5 + 1):
        value = x4 * i**4 + x3 * i**3 + x2 * i**2 + x1 * i + x0
        if value < 0.1:
            value = 0
        data[i] = value


# Главная процедура построения отчета.
def ElevateReportV1(
    excel=None,
    batch_results_path: str | None = None,
    output_folder: str | None = None,
    template_folder: str | None = None,
) -> None:
    excel = excel or get_excel_app()

    excel.ScreenUpdating = False
    excel.DisplayAlerts = False
    try:
        if batch_results_path:
            excel.Workbooks.Open(batch_results_path)
        if excel.ActiveSheet.Name != "batch_results":
            msgbox('Open "batch_results.csv" file!')
            return

        file_name, folder, awt, attd = CopyDataMain(excel)
        job_data, building_data, elevator_data, passenger_data = CopyDataSub(
            file_name, folder, excel
        )

        n_steps = ubound(awt)
        ais = new_1based_list(n_steps, 0)
        alw = new_1based_list(n_steps, 0)
        for i in range(1, n_steps + 1):
            csvParse(file_name, folder, ais, alw, building_data, elevator_data, i, excel)

        PrintData(
            folder,
            awt,
            attd,
            ais,
            alw,
            job_data,
            building_data,
            elevator_data,
            passenger_data,
            excel,
            output_folder=output_folder,
            template_folder=template_folder,
        )
    finally:
        excel.ScreenUpdating = True
        excel.DisplayAlerts = True


