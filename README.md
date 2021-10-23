# ATWS - Application To Windows Service

## Getting Started

ATWS allows you to run any console application as a Windows service.

Create new Windows service. For installation --install and --path is required.

```bash
atws --install "My Console Application Service" --path "C:\Program Files\MyApp\App.exe" --sep ";" --arguments "arg 0;arg 1;arg 2;arg 3"
```

Start Windows service

```bash
atws --start "My Console Application Service"
```

Stop Windows service

```bash
atws --stop "My Console Application Service"
```

Uninstall created Windows service

```bash
atws --uninstall "My Console Application Service"
```
