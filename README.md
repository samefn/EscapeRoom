# ProyectoServicios — Escape Room multijugador de terror

Juego de **escape room cooperativo y competitivo** en primera persona, ambientado en un manicomio abandonado (estilo *SAW*). Se juega en red con **Photon Fusion 2** en modo **player-host**: uno de los jugadores crea la sala y hace de servidor mientras juega.

- **2v2 (modo principal):** dos equipos de dos. En cada equipo, el **Cirujano** está encerrado en el **Quirófano** y el **Operador** en el **Cuarto de máquinas**. Ninguno puede salir solo: tienen que pasarse códigos hablando y objetos por un tubo neumático. **Gana el primer equipo que abre la puerta y apaga la máquina.**
- **1v1:** cada jugador resuelve una sala completa por su cuenta; gana quien termina antes.
- **Prueba de un jugador:** para practicar una sala sin conexión.

Cada partida dura **18 minutos**. Los fallos y las pistas suman "gas"; si se acaba el tiempo, gana el equipo que haya resuelto más puzzles.

---

## Requisitos

| | Versión |
|---|---|
| Unity | **6000.3.16f1** (Unity 6.3) con soporte para Windows |
| Render | URP 17.3 |
| Red | Photon Fusion **2.1.4** (incluido en `Assets/Photon`) |
| Otros paquetes | Input System 1.19, glTFast 6.21 (modelos `.glb`), uGUI |
| Cuenta | Una cuenta gratuita de **Photon** con una App de tipo *Fusion* |

---

## Instalación

1. Clonar el repositorio (o descomprimir el proyecto).
2. Abrir la carpeta con **Unity Hub → Add → Add project from disk** y usar la versión **6000.3.16f1**. La primera apertura tarda: Unity importa los assets y descarga los paquetes.
3. Configurar Photon:
   - Crear una App en el [dashboard de Photon](https://dashboard.photonengine.com) con el SDK **Fusion 2**.
   - En Unity: **Tools → Fusion → Realtime Settings** (o el *Fusion Hub*) y pegar el **App Id**.
   - El App Id es privado: si el repositorio es público, no subas el tuyo (ver "Notas").
4. Comprobar que **File → Build Profiles → Scenes** tiene, en este orden: `Menu`, `CharacterSelect` y `Game`.
5. Menú **Escape Room → 9 - Verificar que el juego se puede resolver**. Debe terminar sin errores.

### Crear una build para jugar con otros
- Menú **Escape Room → 5 - Build de prueba (Windows)**. Genera `Builds/EscapeRoom/EscapeRoom.exe` (antes vuelve a verificar el juego y se cancela si encuentra errores).
- Para compartirla, comprime la carpeta `Builds/EscapeRoom` entera (sin la subcarpeta que termina en `_DoNotShip`). Todos los jugadores deben usar la misma build.
- No hace falta abrir puertos: la conexión pasa por Photon Cloud.

---

## Cómo se juega

### Crear o unirse a una sala
1. Abrir el juego (o `Assets/Scenes/Menu.unity` y **Play** en el editor).
2. **Crear sala:** elegir modo (1v1 o 2v2) y región. Aparece un **código** (por ejemplo `SA-K7QX`) que se pasa al resto.
3. **Unirse a una sala:** escribir el código.
4. Elegir personaje y pulsar **Listo**. La partida empieza cuando están todos.
5. **Prueba de un jugador:** elegir Quirófano o Cuarto de máquinas.

Si un jugador se desconecta a mitad de partida, puede volver entrando con el **mismo código**: recupera su personaje y los puzzles resueltos.

### Controles
| Tecla | Acción |
|---|---|
| **WASD** / ratón | Moverse / mirar |
| **E** o clic | Interactuar con el objeto apuntado |
| **1-5** o rueda | Elegir objeto del inventario |
| **Tab** | Cuaderno (las pistas leídas se guardan solas) |
| **T** | Mensajes rápidos para el compañero (12 frases) |
| **H** | Ayuda "Tu misión" |
| **Esc** | Soltar / capturar el ratón, cerrar ventanas |
| **F3** | Diagnóstico (cámara, avatar, entrada) |

### Los puzzles
| Quirófano (Cirujano) | Cuarto de máquinas (Operador) |
|---|---|
| **Q1** Póster + expediente → código del armario → llave | **M1** 8 etiquetas de las cajas → leyenda de símbolos |
| **Q2** Calentador y tinta térmica → 3 cifras de presión | **M2** Válvulas con la presión del Cirujano → pantalla con símbolos |
| **Q3** Cajones en el orden que dicta el Operador → fusibles | **M3** Llave del colchón → taquilla → palanca |
| **Q4** 3 diapositivas en el proyector → 3 símbolos | **M4** Tanques de 5 y 3 litros: dejar 4 → placa con el orden de cajones |
| **Q5** Teclado de la puerta (6 cifras: símbolos de las dos salas traducidos) | **M5** Fusibles (llegan por el tubo) + interruptores en orden |
| **Q6** Montar la palanca y tirar **a la vez** que el Operador | **M6** Interruptor general **a la vez** que el Cirujano |

Los códigos cambian en cada partida (los genera una semilla que fija el host). En 1v1 y en la prueba de un jugador, unas **notas pegadas en las paredes** sustituyen la información del compañero.

---

## Cómo está hecho

### Arquitectura
- **Una sola escena de juego con cuatro arenas.** `Game.unity` contiene dos copias del Quirófano y dos del Cuarto de máquinas, separadas 200 m. Cada jugador aparece en su propia arena; así todos comparten una sola sesión de red sin cargar escenas distintas.
- **Red (Photon Fusion 2, Host Mode):**
  - `FusionLauncher`: crea o se une a la sala con código y región fija, controla el flujo Menu → CharacterSelect → Game, reparte jugadores en equipos y arenas, y gestiona la reconexión.
  - `PlayerSession` (personaje y "listo") y `PlayerAvatar` (movimiento con `NetworkCharacterController`, animación y cámara en primera persona).
  - `TeamState`: estado compartido de cada equipo (puzzles resueltos, cápsula del tubo, palanca sincronizada, mensajes rápidos, reloj, gas y ganador). Los jugadores piden cambios con RPC y el host los valida.
- **Puzzles locales con datos compartidos.** Cada puzzle (`Assets/Scripts/Puzzles`) es un componente de la arena. Los códigos salen de una semilla común (`PuzzleData`), así que solo viajan por red las acciones, no el contenido de las pistas.
- **Interfaz:** IMGUI con un tema de terror (paneles agrietados, viñeta, grano) en `NightTerrorUi`.
- **Audio:** `Sfx` reproduce efectos por nombre (con tramos dentro de un mismo archivo), ambiente por sala, pasos, gas y avisos.

### Herramientas de editor (menú *Escape Room*)
| Opción | Qué hace |
|---|---|
| 0 - Construir todo | Regenera prefabs, escenas y puzzles |
| 4 - Prefabs de red y escena Menu | Crea los prefabs de red, la escena `Menu` y ordena Build Settings |
| 5 - Build de prueba (Windows) | Verifica el juego y genera la build |
| 6 - Limpiar proyecto | Mueve fuera de `Assets` lo que no usa ninguna escena |
| 7 - Añadir objetos interactuables | Añade `Interactable` y colliders a los objetos de las salas |
| 8 - Importar modelos, audio y colocar puzzles | Configura importaciones, crea materiales y coloca los objetos de los puzzles en las arenas |
| 9 - Verificar que el juego se puede resolver | Comprueba datos, orden de resolución, proyecto, objetos y que todo sea alcanzable |

Las opciones 1-3 generaban arenas, personajes y la selección a partir de las escenas originales del pack; ya no son necesarias.

### Estructura
```
Assets/
  Scenes/          Menu, CharacterSelect, Game
  Scripts/
    Game/          cámara, interacción, arenas, reparto de jugadores, tema de la interfaz
    Characters/    selección de personaje
    Networking/    Fusion: lanzador, sesión, avatar, menú, panel de sala
    Puzzles/       puzzles, inventario, interfaz de puzzles, audio, reloj y gas
  Editor/          menú Escape Room (construcción, colocación y verificación)
  Prefabs/         arenas, personajes y prefabs de red
  Models/          modelos del pack y de los puzzles
  Resources/       audio y tema de la interfaz (se cargan por nombre)
  Photon/          SDK de Photon Fusion
```

### Probar varias instancias en un mismo PC
Abrir varias veces el `.exe`, crear la sala en una y unirse con el código en las demás. Solo la ventana con el foco recibe teclado y ratón. Opciones útiles al lanzar:
- `-logFile log1.txt` (un registro por instancia),
- `-clientid NOMBRE` (identidad para probar la reconexión),
- `-cheats` (F9 da los objetos del compañero y F10 muestra los códigos).

---

## Notas

- **App Id de Photon:** se guarda en `Assets/Photon/Fusion/Resources/PhotonAppSettings.asset`. Si el repositorio es público, conviene hacerlo **privado** o no subir ese archivo (hay una línea preparada en `.gitignore`) y que cada persona ponga su propio App Id.
- **Límites conocidos:** si el host abandona la partida, termina para todos (el host es un jugador). La build es solo para Windows.

---

## Créditos

- **Escenario:** pack gratuito *Karlheinz Asylum* de Lukas Bobor (ver `Assets/Read_me.txt`).
- **Red:** Photon Fusion 2 — Exit Games.
- **Interfaz:** tema *Night Terror Lite* de Julius Schmidt (licencia en `Assets/Resources/NightTerror/LICENSE_NightTerrorLite.txt`). Fuentes *Source Serif 4* e *IBM Plex Mono* bajo SIL Open Font License 1.1.
- **Sonidos:** Freesound.org — ryansnook, noirenex, richerlandtv, diegoverdoodt, bmusic92, ehohnke, poyekhali, freqman, renovatio871, oniwe, jobro, spoonsandlessspoons, dcelliott, nkone_m, inspectorj, salo_c_r_711, rewired, irolan, yummy9987, sheyvan, mattruthsound, isaroru, martian, jrssandoval, newlocknew, spaghetto87, designerschoice, autobot8299, hazelr1ver, pino_naturesounds; y otros efectos de bancos de sonido libres.
- **Modelos de los puzzles:** teclado numérico, caja de fusibles, proyector, televisor, manómetro, volante, pizarra, tubo, lámpara, frascos, tablet, llaves, palanca, placa y monitores, de autores externos (licencias según su página de descarga).
- **Importación glTF:** glTFast (Unity).
