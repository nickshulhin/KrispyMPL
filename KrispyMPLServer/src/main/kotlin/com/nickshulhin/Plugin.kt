package com.nickshulhin

import io.ktor.server.application.*
import io.ktor.server.engine.*
import io.ktor.server.netty.*
import io.ktor.server.response.*
import io.ktor.server.routing.*
import io.ktor.server.websocket.*
import io.ktor.websocket.*
import kotlinx.serialization.Serializable
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import org.slf4j.LoggerFactory
import java.util.concurrent.ConcurrentHashMap

@Serializable
data class PlayerPosition(val name: String, val x: Double, val y: Double, val z: Double, val body: String)

@Serializable
data class ClientMessage(val type: String, val name: String = "", val password: String = "", val x: Double = 0.0, val y: Double = 0.0, val z: Double = 0.0, val body: String = "Kerbin")

@Serializable
data class ServerMessage(val type: String, val players: List<PlayerPosition> = emptyList(), val name: String = "")

private val log = LoggerFactory.getLogger("KrispyMPLServer")

class Room(val password: String) {
    val sessions = ConcurrentHashMap<String, WebSocketSession>()
    val players = ConcurrentHashMap<String, PlayerPosition>()
    val id = if (password.isEmpty()) "public" else password.hashCode().toString(16)
}

fun Application.configurePlugin() {
    install(WebSockets)

    val rooms = ConcurrentHashMap<String, Room>()
    val json = Json { ignoreUnknownKeys = true }

    fun getOrCreateRoom(password: String): Room {
        val key = password.ifEmpty { "" }
        return rooms.getOrPut(key) { Room(password) }
    }

    fun totalPlayers() = rooms.values.sumOf { it.players.size }
    fun roomList() = rooms.values.joinToString(", ") { "${it.id}(${it.players.size})" }

    routing {
        get("/") {
            call.respondText("KrispyMPL Server — ${totalPlayers()} players in ${rooms.size} rooms: ${roomList()}")
        }

        webSocket("/ws") {
            var playerName = "Unknown"
            var currentRoom: Room? = null

            try {
                for (frame in incoming) {
                    if (frame is Frame.Text) {
                        val text = frame.readText()
                        val msg = json.decodeFromString<ClientMessage>(text)

                        when (msg.type) {
                            "join" -> {
                                val room = getOrCreateRoom(msg.password)
                                currentRoom = room

                                if (room.password.isNotEmpty() && msg.password != room.password) {
                                    send(Frame.Text(json.encodeToString(ServerMessage("auth_error", name = "Wrong password"))))
                                    log.info("Player rejected (room ${room.id}, wrong password): ${msg.name}")
                                    close()
                                    return@webSocket
                                }

                                playerName = msg.name
                                room.sessions[playerName] = this
                                room.players[playerName] = PlayerPosition(playerName, 0.0, 0.0, 0.0, "Kerbin")
                                broadcast(json, room, ServerMessage("joined", room.players.values.toList()))
                                log.info("Player joined room ${room.id}: $playerName (${room.players.size} in room, ${totalPlayers()} total)")
                            }

                            "pos" -> {
                                val room = currentRoom ?: return@webSocket
                                val pos = PlayerPosition(playerName, msg.x, msg.y, msg.z, msg.body)
                                room.players[playerName] = pos
                                broadcast(json, room, ServerMessage("player_update", listOf(pos)))
                            }

                            "leave" -> {
                                currentRoom?.let { doLeave(playerName, it, json) }
                            }
                        }
                    }
                }
            } catch (e: Exception) {
                log.info("Connection error for $playerName: ${e.message}")
            } finally {
                currentRoom?.let { room ->
                    doLeave(playerName, room, json)
                    if (room.players.isEmpty() && room.password.isEmpty()) {
                        rooms.remove("")
                    }
                }
            }
        }
    }
}

private suspend fun doLeave(name: String, room: Room, json: Json) {
    if (name == "Unknown") return
    room.sessions.remove(name)
    room.players.remove(name)
    broadcast(json, room, ServerMessage("player_left", name = name))
    log.info("Player left room ${room.id}: $name (${room.players.size} in room)")
}

private suspend fun broadcast(json: Json, room: Room, msg: ServerMessage) {
    val text = json.encodeToString(msg)
    room.sessions.values.forEach { session ->
        try {
            session.send(Frame.Text(text))
        } catch (_: Exception) {}
    }
}

fun main(args: Array<String>) {
    EngineMain.main(args)
}
