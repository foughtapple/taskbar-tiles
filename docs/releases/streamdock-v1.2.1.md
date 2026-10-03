# Taskbar Tiles Stream Dock 1.2.1

Audio Control now mutes every available Windows microphone, and unmute explicitly unmutes every microphone, including ones muted individually beforehand. The button confirms each endpoint's real state and shows mixed, unavailable or failed states honestly. Newly available microphones are muted while global mute is on, including when the button is hidden. Speakers are untouched.

Bounded local diagnostics record delivered button events and each endpoint command/readback to support physical acceptance. Existing Taskbar Tiles actions, placements, preferences and private settings are retained; Taskbar Tiles 0.15.0 remains compatible.

Validation: 50 all-capture policy/failure/lifecycle checks and seven SDK bridge checks passed. The exact archive passed the unchanged installed Taskbar Tiles 0.15.0 module manager in temporary folders. A physical button acceptance test confirmed mute=true on all six available capture endpoints, then mute=false on all six, with no failed endpoint commands. The user confirmed the voice call was silenced while muted and restored on unmute. Live inspection after changing the default microphone confirmed the active call used that newly selected input. No agent-generated live mute commands were sent.
