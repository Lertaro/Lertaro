# Protocolo URI (lertaro://)

En su primera ejecución, Lertaro registra automáticamente el protocolo personalizado **`lertaro://`** en Windows. Enlaces web, accesos directos de escritorio, scripts de automatización y herramientas de terceros pueden utilizar este protocolo para invocar búsquedas, acceder a secciones de configuración o iniciar transferencias inalámbricas.

## 1. Funcionamiento y enrutamiento a instancia única

- **Listo para usar**: No requiere configuración manual en el registro de Windows; Lertaro comprueba y mantiene su registro al iniciarse.
- **Enrutamiento a instancia única**: Si Lertaro ya se está ejecutando en segundo plano, abrir un enlace `lertaro://` redirige la orden a la instancia activa en primer plano sin duplicar procesos. Si no está ejecutándose, Windows inicia la aplicación y ejecuta la acción solicitada.

## 2. Tabla de comandos URI admitidos

| Formato de comando URI | Descripción y resultado visual |
| :--- | :--- |
| `lertaro://` | Activa y muestra la Ventana rápida (equivalente a doble pulsación de `Ctrl`). |
| `lertaro://search/[palabra_clave]` | Abre la Ventana rápida con la `[palabra_clave]` precargada y filtrada. |
| `lertaro://fullsearch/[palabra_clave]` | Abre la Ventana principal con la `[palabra_clave]` precargada. |
| `lertaro://settings/page/[sección]` | Abre Configuración y cambia directamente a la pestaña de nivel superior indicada. |
| `lertaro://settings/entry/[id]` | Abre Configuración y salta a una opción específica resaltándola. |
| `lertaro://localsend` | Abre la ventana vacía de transferencia inalámbrica de LocalSend. |
| `lertaro://localsend/items/[ruta_codificada...]` | Abre LocalSend en modo archivo con una o varias rutas precargadas. |
| `lertaro://localsend/text/[texto_codificado]` | Abre LocalSend en modo texto con el contenido listo para enviar. |

### Secciones de configuración `[sección]`

Los nombres de sección no distinguen mayúsculas y coinciden con la barra lateral de Configuración:

```text
Service      - Estado del servicio
Index        - Configuración de indexación
General      - Configuración general
Appearance   - Apariencia y temas
Hotkeys      - Atajos de teclado
Plugins      - Gestión de plugins
Favorites    - Favoritos
History      - Historial de búsqueda
QuickPanel   - Panel rápido
About        - Acerca de y actualizaciones
```

> [!NOTE]
> El número en `lertaro://settings/entry/[id]` se genera dinámicamente mediante el plugin interno de [**Búsqueda en Configuración**](./instant-answers#2-extensiones-activadas-por-palabra-clave-plugins-integrados). Dado que los identificadores pueden variar entre versiones, se recomienda usar `lertaro://settings/page/[sección]` en scripts y enlaces externos.

## 3. Parámetros de LocalSend y codificación

Al invocar LocalSend mediante URI, cada ruta de archivo o fragmento de texto debe codificarse en formato URL estándar (p. ej. `:` como `%3A`, `\` como `%5C` y espacios como `%20`):

```text
# Precargar varias rutas de archivo
lertaro://localsend/items/C%3A%5CUsers%5Ctestuser%5CDesktop%5Cdoc.pdf/D%3A%5CShared%5Cphotos

# Precargar texto para enviar
lertaro://localsend/text/Hello%20from%20Lertaro%21
```

- **Requisitos de seguridad**: Todas las rutas deben corresponder a rutas absolutas existentes en el equipo local. Los enlaces con datos precargados únicamente abren la selección de dispositivos; nunca inician la transferencia de forma automática.

## 4. Ejemplos de integración externa

### Enlaces en Markdown y bases de conocimiento

Inserta enlaces directos en Obsidian, Notion o documentos Markdown:

```markdown
Abrir [Configuración de apariencia de Lertaro](lertaro://settings/page/Appearance)
Buscar [Estados financieros](lertaro://search/informe%20financiero%202026)
```

### Accesos directos y scripts de Windows

Crea un acceso directo en el escritorio y escribe en la ubicación del destino:

```cmd
lertaro://fullsearch/D:\Projects\
```

Invocación desde PowerShell:

```powershell
Start-Process "lertaro://settings/page/General"
```

## 5. Seguridad y tolerancia ante rutas desconocidas

- **Gestión segura y silenciosa**: Dado que cualquier página web o aplicación puede invocar el protocolo, Lertaro valida de forma estricta todas las solicitudes URI. Las rutas con formato incorrecto o inexistentes se ignoran de forma segura y se registran en los registros de depuración sin generar fallos ni comportamientos inesperados.
