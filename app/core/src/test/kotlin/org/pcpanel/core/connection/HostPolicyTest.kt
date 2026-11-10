package org.pcpanel.core.connection

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNotNull
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Test

class HostPolicyTest {
    @Test
    fun aceita_loopback_e_rede_local() {
        for (h in listOf(
            "127.0.0.1", "127.1.2.3", "192.168.0.12", "10.0.0.5", "172.16.0.1", "172.31.255.255", "169.254.1.1",
            "localhost", "LOCALHOST", "pc-casa.local", "meu.pc.local", "::1", "[::1]", "fe80::1", "fe80::1%wlan0", "fd12:3456::1",
        )) assertNull(HostPolicy.check(Endpoint(h, 8765)), h)
    }

    @Test
    fun recusa_o_resto() {
        for (h in listOf("8.8.8.8", "172.32.0.1", "172.15.0.1", "192.169.0.1", "11.0.0.1", "1.2.3.999", "example.com", "meu-pc", "", "  ", "2001:db8::1", ".local", "a..local")) {
            assertNotNull(HostPolicy.check(Endpoint(h, 8765)), h)
        }
    }

    @Test
    fun porta_precisa_ser_valida() {
        assertNotNull(HostPolicy.check(Endpoint("127.0.0.1", 0)))
        assertNotNull(HostPolicy.check(Endpoint("127.0.0.1", 70000)))
        assertNull(HostPolicy.check(Endpoint("127.0.0.1", 65535)))
    }

    @Test
    fun urls() {
        assertEquals("ws://192.168.0.12:8765/ws", Endpoint("192.168.0.12", 8765).wsUrl)
        assertEquals("http://127.0.0.1:9000", Endpoint("127.0.0.1", 9000).httpBase)
        assertEquals("ws://[::1]:8765/ws", Endpoint("::1", 8765).wsUrl)
    }

    @Test
    fun le_endereco_com_ou_sem_porta() {
        assertEquals(Endpoint("192.168.0.12", 8765), Endpoint.parse("192.168.0.12"))
        assertEquals(Endpoint("192.168.0.12", 9000), Endpoint.parse(" 192.168.0.12:9000 "))
        assertEquals(Endpoint("::1", 8765), Endpoint.parse("[::1]"))
        assertEquals(Endpoint("::1", 9000), Endpoint.parse("[::1]:9000"))
        assertEquals(Endpoint("fe80::1", 8765), Endpoint.parse("fe80::1"))
        assertNull(Endpoint.parse(""))
        assertNull(Endpoint.parse("host:abc"))
        assertNull(Endpoint.parse("[::1"))
        assertNull(Endpoint.parse("[::1]x"))
    }

    @Test
    fun lista_de_enderecos_e_ida_e_volta_da_configuracao() {
        assertEquals(listOf(Endpoint("127.0.0.1", 8765), Endpoint("192.168.0.12", 8765)), Endpoint.parseList("127.0.0.1:8765, 192.168.0.12,"))
        assertNull(Endpoint.parseList(""))
        assertNull(Endpoint.parseList("127.0.0.1, host:abc"))
        for (e in listOf(Endpoint("127.0.0.1", 8765), Endpoint("::1", 9000), Endpoint("pc.local", 1))) {
            assertEquals(e, Endpoint.parse(e.toConfigString()))
        }
    }
}
