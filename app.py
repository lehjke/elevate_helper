import tkinter 
import body
import app_icon
n_copy = 0
buildingtype = ""

def button_click(buildingtype,morningflag) -> None:
    path = path_entry.get()
    global n_copy
    try:
        body.main(n_copy, path, buildingtype, morningflag)
    except Exception as ex:
        template = "An exception of type {0} occurred in main(). Arguments:\n{1!r}"
        message = template.format(type(ex).__name__, ex.args)
        error_state_val.set(message)


def print_report() -> None:
    path = path_entry.get()
    try:
        body.print_report(path)
    except Exception as ex:
        template = "An exception of type {0} occurred in print_report(). Arguments:\n{1!r}"
        message = template.format(type(ex).__name__, ex.args)
        error_state_val.set(message)

def print_report_morning() -> None:
    path = path_entry.get()
    try:
        body.print_report(path + '//' + 'morning')
    except Exception as ex:
        template = "An exception of type {0} occurred in print_report_morning(). Arguments:\n{1!r}"
        message = template.format(type(ex).__name__, ex.args)
        error_state_val.set(message)

def print_report_lunch() -> None:
    path = path_entry.get()
    try:
        body.print_report(path + '//' + 'lunch')
    except Exception as ex:
        template = "An exception of type {0} occurred in print_report_lunch(). Arguments:\n{1!r}"
        message = template.format(type(ex).__name__, ex.args)
        error_state_val.set(message)

def choosetype(type) -> None:
    global n_copy
    global buildingtype
    buildingtype = type
    if type == 'Office':
        office_button.select()
        residence_button.deselect()
        hotel_button.deselect()
        n_copy = 13
        report_button.grid_forget()
        report_button_morning.grid(column=0, row=1, padx=(10, 5), pady=5, sticky='news', columnspan=2)
        report_button_lunch.grid(column=2, row=1, padx=(5, 10), pady=5, sticky='news', columnspan=2)
        run_morning.grid(column=1, row=0, padx=(10, 5), pady=5, sticky='news', columnspan=1)
        run_button.grid(columnspan = 1)
    elif type == 'Residence':
        residence_button.select()
        office_button.deselect()
        hotel_button.deselect()
        n_copy = 8
        report_button_morning.grid_forget()
        report_button_lunch.grid_forget()
        report_button.grid(column=0, row=1, columnspan=4, padx=10, pady=5, ipadx=70, sticky='news')
        run_morning.grid_forget()
        run_button.grid(columnspan = 2)
    elif type == 'Hotel':
        hotel_button.select()
        office_button.deselect()
        residence_button.deselect()
        n_copy = 13
        report_button_morning.grid_forget()
        report_button_lunch.grid_forget()
        report_button.grid(column=0, row=1, columnspan=4, padx=10, pady=5, ipadx=70, sticky='news')
        run_morning.grid_forget()
        run_button.grid(columnspan = 2)


icon = app_icon.icon_64()

root = tkinter.Tk()
root.title('Elevate Helper')
root.iconphoto(True, tkinter.PhotoImage(data=icon))

frame_1 = tkinter.LabelFrame(root)
frame_1.pack(padx=10, pady=10, fill='both', expand=True)

frame_1.rowconfigure(0, weight=1)
frame_1.rowconfigure(1, weight=1)
frame_1.rowconfigure(2, weight=1)
frame_1.rowconfigure(3, weight=1)
frame_1.columnconfigure(0, weight=1)
#frame_1.columnconfigure(1, weight=1)

# n_copy_label = tkinter.Label(frame_1, text='Number of copies to make:', width=30, anchor='w')
# n_copy_label.grid(column=0, row=0, padx=10, pady=5, sticky='news')
# n_copy_entry = tkinter.Entry(frame_1, width=35, borderwidth=2)
# n_copy_entry.grid(column=0, row=1, padx=10, pady=5, sticky='news')

path_label = tkinter.Label(frame_1, text='Path to the Elevate file:', width=30, anchor='w')
path_label.grid(column=0, row=2, padx=10, pady=5, sticky='news')
path_entry = tkinter.Entry(frame_1, width=35, borderwidth=2)
path_entry.grid(column=0, row=3, padx=10, pady=5, sticky='news')



frame_3 = tkinter.LabelFrame(root)
frame_3.pack(padx=10, pady=10, fill='both', expand=True)

frame_3.rowconfigure(0, weight=1)
frame_3.columnconfigure(0, weight=1)
frame_3.columnconfigure(1, weight=1)
frame_3.columnconfigure(2, weight=1)

office_button = tkinter.Checkbutton(frame_3, text='Office', command=lambda: choosetype('Office'), padx=15, pady=5,cursor="hand2")
office_button.grid(column=0, row=0, padx=(10, 5), pady=5, sticky='news', columnspan=1)

residence_button = tkinter.Checkbutton(frame_3, text='Residence', command=lambda: choosetype('Residence'), padx=15, pady=5,cursor="hand2")
residence_button.grid(column=1, row=0, padx=(5, 10), pady=5, sticky='news', columnspan=1)

hotel_button = tkinter.Checkbutton(frame_3, text='Hotel', command=lambda: choosetype('Hotel'), padx=15, pady=5,cursor="hand2")
hotel_button.grid(column=2, row=0, padx=(5, 10), pady=5, sticky='news', columnspan=1)

frame_2 = tkinter.LabelFrame(root)
frame_2.pack(padx=10, pady=10, fill='both', expand=True)

frame_2.rowconfigure(0, weight=1)
frame_2.rowconfigure(1, weight=1)
frame_2.columnconfigure(0, weight=1)
frame_2.columnconfigure(1, weight=1)
frame_2.columnconfigure(2, weight=1)
frame_2.columnconfigure(3, weight=1)

run_button = tkinter.Button(frame_2, text='Run', command=lambda: button_click(buildingtype,1), padx=10, pady=5,cursor="hand2")
run_button.grid(column=0, row=0, padx=(10, 5), pady=5, sticky='news', columnspan=2)
run_morning=tkinter.Button(frame_2, text='Run \n morning \n only', command=lambda: button_click(buildingtype,0), padx=0, pady=5,cursor="hand2")

exit_button = tkinter.Button(frame_2, text='Exit', command=root.destroy, padx=10, pady=5,cursor="hand2")
exit_button.grid(column=2, row=0, padx=(5, 10), pady=5, sticky='news', columnspan=2)


report_button = tkinter.Button(frame_2, text='Print report', command=print_report, padx=5, pady=5,cursor="hand2")
report_button_morning = tkinter.Button(frame_2, text='Print morning report', command=print_report_morning, padx=5, pady=5,cursor="hand2")
report_button_lunch = tkinter.Button(frame_2, text='Print lunch report', command=print_report_lunch, padx=5, pady=5,cursor="hand2")

frame_4 = tkinter.LabelFrame(root)
frame_4.pack(padx=10, pady=10, fill='both', expand=True)

frame_4.rowconfigure(0, weight=1)
frame_4.columnconfigure(0, weight=1)
frame_4.columnconfigure(1, weight=1)

error_state_val = tkinter.StringVar()
error_state_val.set('OK!')

error_label = tkinter.Label(frame_4, text='Checkup:')
error_label.grid(column=0, row=0, padx=10, pady=5, sticky='w')

error_state = tkinter.Label(frame_4, textvariable=error_state_val,justify='right',width=35,wraplength=250,anchor="e")
error_state.grid(column=1, row=0, padx=10, pady=5, sticky='e')

root.mainloop()
