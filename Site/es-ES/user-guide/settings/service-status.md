# Estado del Servicio

Gestiona el servicio de indexación en segundo plano, y te da una vista en vivo de sus registros.

## Control del servicio

Una tarjeta de estado muestra el estado actual del indexador elevado — **Instalando**, **Indexando**, **Listo**
(con un recuento en vivo de archivos/carpetas), o **Error** — con un icono a juego (spinner, marca de verificación,
o insignia de error).

Un botón **Instalar e iniciar servicio** aparece solo cuando el servicio aún no está instalado o ha tenido un
error; una vez instalado y en ejecución, no hay ningún control manual de inicio/detención/desinstalación en esta
página — se espera que el servicio simplemente siga funcionando en segundo plano.

## Registros

Tres pestañas — **App**, **Hook**, **Service** — correspondientes a los tres procesos que ejecuta Lertaro (el
indexador elevado en segundo plano, la App por usuario con la que interactúas, y el proceso de hook de teclado).
Cada pestaña muestra las líneas de registro de ese proceso, coloreadas según el nivel.

- Desplegable de **filtro de nivel** — Todos / Error / Warn / Info / Debug.
- **Cuadro de búsqueda** — filtra las líneas visibles por palabra clave, combinado con el filtro de nivel.
- Botón **Borrar** — vacía el registro de la pestaña seleccionada en ese momento. Borrar el registro de la pestaña
  Service se enruta a través del propio servicio (el proceso de la App no tiene permiso para escribir en él
  directamente); los registros de App y Hook se borran directamente, ya que son archivos por usuario.

Este es el primer lugar donde mirar al solucionar problemas — ver [Solución de
problemas](../troubleshooting#¿sigues-atascado).
