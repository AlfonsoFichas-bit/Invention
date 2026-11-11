![Logo de Invention Comics](logo.svg)

# Invention Comics

Aplicación de escritorio para organizar y leer cómics en Windows.

## Funciones

- Añadir cómics CBR y ZIP a la biblioteca.
- Buscar y gestionar los cómics añadidos.
- Leer cómics y cambiar entre páginas.
- Adaptar el logo al tema claro u oscuro de Windows.

## Instalación

### Con Scoop (Recomendado)

Si usas [Scoop](https://scoop.sh/), puedes añadir este repositorio como bucket e instalar la aplicación fácilmente:

```powershell
# 1. Añadir el bucket
scoop bucket add invention https://github.com/AlfonsoFichas-bit/Invention.git

# 2. Instalar
scoop install invention
```

O si prefieres instalarla directamente sin agregar el bucket:

```powershell
scoop install https://raw.githubusercontent.com/AlfonsoFichas-bit/Invention/master/bucket/invention.json
```

Para actualizar la aplicación a futuras versiones:

```powershell
scoop update invention
```

### Descarga directa (Portable)

1. Ve a la sección de [Releases](https://github.com/AlfonsoFichas-bit/Invention/releases).
2. Descarga el archivo `Invention-v*-win-x64.zip`.
3. Descomprímelo en la carpeta que desees y ejecuta `Invention.exe`.

## Tecnologías

- .NET 8
- WinUI 3

## Compilar

Abre `Invention.slnx` en Visual Studio y compila el proyecto `Invention`.
