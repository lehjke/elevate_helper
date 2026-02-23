import py_win_keyboard_layout

import os
import shutil

import time
import keyboard

import win32com.client
import win32gui

import xml.etree.ElementTree as ET
from xml.dom import minidom

import csv
import report_lib

def main(n_copy, path, buildingtype, morningflag) -> None:
    #n_copy=3
    #path = r'C:\Users\ALesnichiy\Desktop\elevate\testele'
    print(path)
    n_copy = int(n_copy)
    try:
        makecopiesandrun(buildingtype, path, n_copy,morningflag) 
    except Exception as ex:
        template = "An exception of type {0} occurred in makecopiesandrun(). Arguments:\n{1!r}"
        message = template.format(type(ex).__name__, ex.args)
        print(message)
    if buildingtype == "Office":
        try:
            if morningflag == 1:
                get_area(os.path.join(path, "lunch"))
            get_area(os.path.join(path, "morning"))
        except Exception as ex:
            template = "An exception of type {0} occurred in get_area(). Arguments:\n{1!r}"
            message = template.format(type(ex).__name__, ex.args)
            print(message)
    else:
        try:
            get_area(path)
        except Exception as ex:
            template = "An exception of type {0} occurred in get_area() (not office one). Arguments:\n{1!r}"
            message = template.format(type(ex).__name__, ex.args)
            print(message)
    


def print_report(path) -> None:
    if not path:
        return

    batch_results_path = os.path.join(path, "batch_results.csv")
    if not os.path.exists(batch_results_path):
        print(f"batch_results.csv not found: {batch_results_path}")
        return

    excel_app = report_lib.get_excel_app(visible=True, use_active=False)
    win32gui.SetForegroundWindow(excel_app.Hwnd)

    report_lib.ElevateReportV1(
        excel=excel_app,
        batch_results_path=batch_results_path,
        output_folder=path,
    )

def modify_handling_capacity(xml_file, new_capacity):

     # Parse the XML file
    tree = ET.parse(xml_file)
    root = tree.getroot()
    
    # Find and modify HandlingCapacity in PassengerData/Standard
    data_standard = root.find('.//PassengerData/Standard')
    if data_standard is not None:
        handling_capacity = data_standard.find('HandlingCapacity')
        if handling_capacity is not None:
            handling_capacity.text = str(new_capacity)
    
     # Find and modify TotalArrivalRate in PassengerData/Traffic
    data_traffic = root.find('.//PassengerData/Traffic')
    if data_traffic is not None:
        data_periods = data_traffic.findall('Period')
        for data_period in data_periods:
            if data_period.get('Id') == '0':
                data_period.set('TotalArrivalRate', str(new_capacity))

  # Save the modified XML with pretty formatting
    xml_str = ET.tostring(root, encoding='utf-8')
    pretty_xml = minidom.parseString(xml_str).toprettyxml(indent="  ")
    
    with open(xml_file, 'w', encoding='utf-8') as f:
        f.write(pretty_xml)

def modify_buildingtype_office(xml_file, peak) -> None:
    tree = ET.parse(xml_file)
    root = tree.getroot()

    data_traffic = root.find('.//PassengerData/Traffic')
    if data_traffic is not None:
        data_periods = data_traffic.findall('Period')
        for data_period in data_periods:
            if data_period.get('Id') == '0':
                if peak == 'Morning':
                    data_period.set('SplitUp', "100")
                    data_period.set('SplitDown',"0")
                    data_period.set('SplitInterfloor',"0")
                elif peak == 'Lunch':
                    data_period.set('SplitUp', "45")
                    data_period.set('SplitDown',"45")
                    data_period.set('SplitInterfloor',"10")
                else: 
                    print('Unknown peak')

    data_btype = root.find('.//BuildingData')
    if data_btype is not None:
        data_btype.set('BuildingType', "1")

    xml_str = ET.tostring(root, encoding='utf-8')
    pretty_xml = minidom.parseString(xml_str).toprettyxml(indent="  ")
    
    with open(xml_file, 'w', encoding='utf-8') as f:
        f.write(pretty_xml)

def modify_buildingtype_residence(xml_file, buildingtype) -> None:
    tree = ET.parse(xml_file)
    root = tree.getroot()

    data_traffic = root.find('.//PassengerData/Traffic')
    if data_traffic is not None:
        data_periods = data_traffic.findall('Period')
        for data_period in data_periods:
            if data_period.get('Id') == '0':
                data_period.set('SplitUp',"50")
                data_period.set('SplitDown',"50")
                data_period.set('SplitInterfloor',"0")

    try:
        data_btype = root.find('.//BuildingData')
        if data_btype is not None:
            if buildingtype == "Residence":
                data_btype.set('BuildingType', '3')
            elif buildingtype == "Hotel":
                data_btype.set('BuildingType', '2')
            else:
                print("Unknown building type")
    except Exception as ex:
        template = "An exception of type {0} occurred in btype change. Arguments:\n{1!r}"
        message = template.format(type(ex).__name__, ex.args)
        print(message)
    
    xml_str = ET.tostring(root, encoding='utf-8')
    pretty_xml = minidom.parseString(xml_str).toprettyxml(indent="  ")
    with open(xml_file, 'w', encoding='utf-8') as f:
        f.write(pretty_xml) 

def modifytitle(xml_file,peak) -> None:
    tree = ET.parse(xml_file)
    root = tree.getroot()

    data_job = root.find('.//JobData')
    if data_job is not None:
        if peak == 'Lunch':
            mod_data_job = str(data_job.get('JobTitle')) + " (обеденный пик)"
        else:
             mod_data_job = str(data_job.get('JobTitle')) + " (утренний пик)"
        data_job.set('JobTitle', mod_data_job)


    xml_str = ET.tostring(root, encoding='utf-8')
    pretty_xml = minidom.parseString(xml_str).toprettyxml(indent="  ")
    
    with open(xml_file, 'w', encoding='utf-8') as f:
        f.write(pretty_xml)

def get_area(path) -> None:
    file = [f for f in os.listdir(path) if f.lower().endswith("01.elvx")]
    xml_file = os.path.join(path, file[0])
    # xml_file = os.path.join(path, os.listdir(path)[0])
    csv_file = os.path.join(path, 'floor_area.csv')

    tree = ET.parse(xml_file)
    root = tree.getroot()

    data = []

    # Find FloorArea in ElevatorData/Advanced/Configuration/Car
    data_cars = root.findall('.//ElevatorData/Advanced/Configuration/Car')
    if data_cars is not None:
        for data_car in data_cars:
            car_id = data_car.get('Id')
            floor_area = data_car.get('FloorAreaM2')

            data.append({
                'CarId' : car_id,
                'FloorAreaM2' : floor_area
            })

    #Write CSV
    if data:
        fieldnames = ['CarId', 'FloorAreaM2']

        with open(csv_file, 'w', newline='', encoding='utf-8') as f:
            writer = csv.DictWriter(f, fieldnames=fieldnames, delimiter=';')
            writer.writeheader()
            writer.writerows(data)

def resedencerun(path) -> None:
    os.startfile(r'C:\Program Files (x86)\Elevate 9\Elevate.exe')
    time.sleep(1.5)
    py_win_keyboard_layout.change_foreground_window_keyboard_layout(0x04090409)
    keyboard.press_and_release('alt + a')
    keyboard.press_and_release('down arrow')
    keyboard.press_and_release('down arrow')
    keyboard.press_and_release('down arrow')
    keyboard.press_and_release('down arrow')
    keyboard.press_and_release('down arrow')
    keyboard.press_and_release('enter')
    time.sleep(0.5)
    keyboard.press_and_release('tab')
    keyboard.press_and_release('tab')
    keyboard.press_and_release('tab')
    time.sleep(0.5)
    #keyboard.press_and_release('ctrl + v')
    keyboard.write(path)
    keyboard.press_and_release('enter')
    print("Residence launched")

def officerun(path,morningflag) -> None:
    os.startfile(r'C:\Program Files (x86)\Elevate 9\Elevate.exe')
    time.sleep(1.5)
    py_win_keyboard_layout.change_foreground_window_keyboard_layout(0x04090409)
    keyboard.press_and_release('alt + a')
    keyboard.press_and_release('down arrow')
    keyboard.press_and_release('down arrow')
    keyboard.press_and_release('down arrow')
    keyboard.press_and_release('down arrow')
    keyboard.press_and_release('down arrow')
    keyboard.press_and_release('enter')
    time.sleep(0.5)
    keyboard.press_and_release('tab')
    keyboard.press_and_release('tab')
    keyboard.press_and_release('tab')
    time.sleep(0.5)
    #keyboard.press_and_release('ctrl + v')
    keyboard.write(os.path.join(path, 'morning'))
    keyboard.press_and_release('enter')
    print("Morning launched")
    if morningflag == 1:
        os.startfile(r'C:\Program Files (x86)\Elevate 9\Elevate.exe')
        time.sleep(1.5)
        py_win_keyboard_layout.change_foreground_window_keyboard_layout(0x04090409)
        keyboard.press_and_release('alt + a')
        keyboard.press_and_release('down arrow')
        keyboard.press_and_release('down arrow')
        keyboard.press_and_release('down arrow')
        keyboard.press_and_release('down arrow')
        keyboard.press_and_release('down arrow')
        keyboard.press_and_release('enter')
        time.sleep(0.5)
        keyboard.press_and_release('tab')
        keyboard.press_and_release('tab')
        keyboard.press_and_release('tab')
        time.sleep(0.5)
        #keyboard.press_and_release('ctrl + v')
        keyboard.write(os.path.join(path, 'lunch'))
        keyboard.press_and_release('enter')
        print("Lunch launched")

def makecopiesandrun(buildingtype, path, n_copy, morningflag) -> None:
    file = [f for f in os.listdir(path) if f.lower().endswith(".elvx")]
    lunchpath = ''
    morningpath = ''
    
    if buildingtype == 'Residence' or buildingtype == 'Hotel':
        modify_buildingtype_residence(os.path.join(path, file[0]),buildingtype) 
        for i in range(2, n_copy + 1):
            if i < 10:
                shutil.copyfile(
                    os.path.join(path, file[0]),
                    os.path.join(path, file[0][:-6] + str(i) + '.elvx'),
                )
            else:
                shutil.copyfile(
                    os.path.join(path, file[0]),
                    os.path.join(path, file[0][:-7] + str(i) + '.elvx'),
                )
            file = [f for f in os.listdir(path) if f.lower().endswith(".elvx")]
            modify_handling_capacity(os.path.join(path, file[i-1]), i)
        resedencerun(path)

    elif buildingtype == 'Office':
        morningpath = os.path.join(path, 'morning')
        os.makedirs(morningpath, exist_ok=True)
        shutil.copyfile(os.path.join(path, file[0]), os.path.join(morningpath, file[0]))
        if morningflag == 1:
            lunchpath = os.path.join(path, 'lunch')
            os.makedirs(lunchpath, exist_ok=True)
            shutil.copyfile(os.path.join(path, file[0]), os.path.join(lunchpath, file[0]))
            modify_buildingtype_office(os.path.join(lunchpath, file[0]),'Lunch')  
            modifytitle(os.path.join(lunchpath, file[0]),"Lunch")
        modify_buildingtype_office(os.path.join(morningpath, file[0]),'Morning') 
        modifytitle(os.path.join(morningpath, file[0]),"Morning")
        print(os.path.join(morningpath, file[0]))
        for i in range(2, n_copy + 1):
            if i < 10:
                shutil.copyfile(
                    os.path.join(morningpath, file[0]),
                    os.path.join(morningpath, file[0][:-6] + str(i) + '.elvx'),
                )
                if morningflag == 1:
                    shutil.copyfile(
                        os.path.join(lunchpath, file[0]),
                        os.path.join(lunchpath, file[0][:-6] + str(i) + '.elvx'),
                    )
            else:
                shutil.copyfile(
                    os.path.join(morningpath, file[0]),
                    os.path.join(morningpath, file[0][:-7] + str(i) + '.elvx'),
                )
                if morningflag == 1:
                    shutil.copyfile(
                        os.path.join(lunchpath, file[0]),
                        os.path.join(lunchpath, file[0][:-7] + str(i) + '.elvx'),
                    )
            file = os.listdir(morningpath)
            modify_handling_capacity(os.path.join(morningpath, file[i-1]), i)
            if morningflag == 1:
                file = os.listdir(lunchpath)
                modify_handling_capacity(os.path.join(lunchpath, file[i-1]), i) 
        officerun(path,morningflag) 

    else:
        print('Unknown building type')

