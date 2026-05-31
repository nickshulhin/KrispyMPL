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
data class ClientMessage(val type: String, val name: String = "", val x: Double = 0.0, val y: Double = 0.0, val z: Double = 0.0, val body: String = "Kerbin")

@Serializable
data class ServerMessage(val type: String, val players: List<PlayerPosition> = emptyList(), val name: String = "")

private val log = LoggerFactory.getLogger("KspServer")

fun Application.configurePlugin() {
    install(WebSockets)

    val sessions = ConcurrentHashMap<String, WebSocketSession>()
    val players = ConcurrentHashMap<String, PlayerPosition>()
    val json = Json { ignoreUnknownKeys = true }

    routing {
        get("/") {
            call.respondText("KSP Multiplayer Server - ${players.size} players connected")
        }

        webSocket("/ws") {
            var playerName = "Unknown"

            try {
                for (frame in incoming) {
                    if (frame is Frame.Text) {
                        val text = frame.readText()
                        val msg = json.decodeFromString<ClientMessage>(text)

                        when (msg.type) {
                            "join" -> {
                                playerName = msg.name
                                sessions[playerName] = this
                                players[playerName] = PlayerPosition(playerName, 0.0, 0.0, 0.0, "Kerbin")
                                broadcast(json, sessions, ServerMessage("joined", players.values.toList()))
                                log.info("Player joined: $playerName (${players.size} online)")
                            }

                            "pos" -> {
                                val pos = PlayerPosition(playerName, msg.x, msg.y, msg.z, msg.body)
                                players[playerName] = pos
                                broadcast(json, sessions, ServerMessage("player_update", listOf(pos)))
                            }

                            "leave" -> {
                                doLeave(playerName, sessions, players, json)
                            }
                        }
                    }
                }
            } catch (e: Exception) {
                log.info("Connection error for $playerName: ${e.message}")
            } finally {
                doLeave(playerName, sessions, players, json)
            }
        }
    }
}

private suspend fun doLeave(name: String, sessions: ConcurrentHashMap<String, WebSocketSession>, players: ConcurrentHashMap<String, PlayerPosition>, json: Json) {
    if (name != "Unknown") {
        sessions.remove(name)
        players.remove(name)
        broadcast(json, sessions, ServerMessage("player_left", name = name))
        log.info("Player left: $name (${players.size} online)")
    }
}

private suspend fun broadcast(json: Json, sessions: ConcurrentHashMap<String, WebSocketSession>, msg: ServerMessage) {
    val text = json.encodeToString(msg)
    sessions.values.forEach { session ->
        try {
            session.send(Frame.Text(text))
        } catch (_: Exception) {}
    }
}

fun main(args: Array<String>) {
    EngineMain.main(args)
}
