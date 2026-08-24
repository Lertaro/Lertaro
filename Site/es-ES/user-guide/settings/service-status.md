# Estado del servicio

La página de Estado del servicio supervisa la salud del servicio de indexación en segundo plano de Lertaro e integra visores de registro en tiempo real para los procesos Service, App y Hook. La página se encuentra en **Configuración → Estado del servicio**.

## 1. Control y estado del servicio

La tarjeta superior muestra el estado del servicio de Windows con privilegios elevados:

- **Indicadores de estado**:
  - **Listo (Ready)**: El servicio funciona correctamente y muestra el recuento de archivos y carpetas indexados.
  - **Indexando (Indexing)**: Escaneo inicial o reconstrucción en curso (con indicador animado).
  - **Instalando (Installing)**: Registro y arranque del servicio de Windows.
  - **Error**: El servicio no responde o ha fallado (con distintivo de advertencia y descripción).
- **Autorrecuperación e instalación**: El botón **Instalar e iniciar servicio** aparece únicamente si el servicio no está instalado o se encuentra en estado de error. En funcionamiento normal, permanece siempre en segundo plano.

## 2. Visor de registros en vivo para tres procesos

La consola inferior está dividida en tres pestañas que corresponden a los procesos de Lertaro:

- **Pestaña App**: Registros de la interfaz de usuario WPF, interacción de búsqueda y atajos.
- **Pestaña Hook**: Eventos del proceso de interceptación de teclado de bajo nivel (`Lertaro.Hook.exe`).
- **Pestaña Service**: Procesamiento del diario USN, escaneo de disco, árbol en memoria y comunicaciones IPC del servicio (`Lertaro.Service.exe`).

### Filtrado y mantenimiento

- **Filtro por nivel**: Permite seleccionar "Todos / Error / Advertencia / Información / Depuración" con líneas coloreadas.
- **Búsqueda en tiempo real**: Filtra líneas de registro mediante palabras clave.
- **Limpieza segura**: Pulsa **Borrar registros** para vaciar la pestaña activa. Los registros del servicio se truncan de forma segura a través de IPC.
